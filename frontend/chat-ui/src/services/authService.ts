import { RenewalFailedError, SessionExpiredError } from './sessionErrors'

export interface TokenDto {
  accessToken: string
  expiresAt: string
  userId: string
}

const TOKEN_KEY = 'auth_token'
const TOKEN_EXPIRY_KEY = 'auth_token_expiry'

/**
 * Records that a session was established, and which kind. The refresh credential is an
 * http-only cookie that script cannot see, so without this the client cannot tell "never
 * signed in" from "access token expired and was cleared" — and would show the sign-in form
 * to someone whose refresh cookie is still perfectly good.
 */
const SESSION_KEY = 'auth_session'

const ORDINARY = 'ordinary'
const REMEMBERED = 'remembered'

/**
 * A companion cookie whose only job is to expire at the same moment the refresh cookie of an
 * unremembered session does. Script can read it, unlike the credential itself, so it answers
 * the one question localStorage cannot: is this still the browsing session the user signed in
 * during, or did the browser restart and take the credential with it?
 *
 * It has to be a cookie rather than sessionStorage because sessionStorage is scoped to a tab.
 * A second tab starts with an empty one and would look like a browser restart, which is
 * exactly the bug this replaces. Cookies are shared across tabs and discarded on browser
 * close — the same lifetime as the credential it stands in for. It carries no secret.
 */
const LIVE_COOKIE = 'auth_session_live'

/**
 * How close to expiry an access token may get before it is renewed. A margin rather than
 * zero so renewal happens before a request can fail, and wide enough to absorb clock skew
 * between the browser and the API.
 *
 * Access tokens are short-lived by design — see Jwt.ExpiryMinutes — so this stays well under
 * that lifetime: a margin approaching it would renew almost every request.
 */
const RENEWAL_MARGIN_MS = 60_000

function markLive(): void {
  // No expires and no max-age: a browser-session cookie, gone when the browser closes.
  document.cookie = `${LIVE_COOKIE}=1; path=/; samesite=lax`
}

function clearLive(): void {
  document.cookie = `${LIVE_COOKIE}=; path=/; samesite=lax; expires=Thu, 01 Jan 1970 00:00:00 GMT`
}

function isLive(): boolean {
  return document.cookie
    .split(';')
    .some((c) => c.trim().startsWith(`${LIVE_COOKIE}=1`))
}

class AuthService {
  /** The one renewal in flight, shared by every concurrent caller. */
  private _refreshPromise: Promise<string> | null = null
  private _refreshGeneration = 0

  async login(email: string, password: string, staySignedIn = false): Promise<TokenDto> {
    const response = await fetch('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // The refresh credential comes back as an http-only cookie, which the browser only
      // stores when the request is made with credentials.
      credentials: 'include',
      body: JSON.stringify({ email, password, staySignedIn }),
    })

    if (!response.ok) {
      throw new Error(`${response.status}`)
    }

    const dto: TokenDto = await response.json()
    this._store(dto, staySignedIn)
    return dto
  }

  async register(
    email: string,
    password: string,
    displayName: string,
    staySignedIn = false,
  ): Promise<TokenDto> {
    const response = await fetch('/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ email, password, displayName, staySignedIn }),
    })

    if (!response.ok) {
      throw new Error(`${response.status}`)
    }

    const dto: TokenDto = await response.json()
    this._store(dto, staySignedIn)
    return dto
  }

  /**
   * Signs out on the server as well as locally, so the refresh credential is revoked rather
   * than left live. Local state is cleared even if the server cannot be reached — being
   * offline must not leave the user apparently signed in.
   */
  async logout(): Promise<void> {
    try {
      await fetch('/auth/logout', { method: 'POST', credentials: 'include' })
    } catch {
      // Ignored deliberately; see above.
    } finally {
      this.clearLocal()
    }
  }

  /** Discards the stored session without contacting the server. */
  clearLocal(): void {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(TOKEN_EXPIRY_KEY)
    localStorage.removeItem(SESSION_KEY)
    clearLive()
  }

  /**
   * Whether a session was established and has not been ended. True while the refresh cookie
   * is expected to still exist, even once the access token has lapsed.
   */
  hasSession(): boolean {
    const kind = localStorage.getItem(SESSION_KEY)
    if (!kind) return false

    // An unremembered session lasts exactly as long as the browsing session. Once the
    // companion cookie is gone the credential is too, so the record is stale rather than
    // useful — discard it instead of leaving a renewal to be refused later.
    if (kind === ORDINARY && !isLive()) {
      this.clearLocal()
      return false
    }

    // Anything else, including the marker written before kinds existed, is treated as
    // remembered: renewing may fail, but refusing to try would sign a user out for a
    // deploy they had nothing to do with.
    return true
  }

  /**
   * Restores a session whose access token has lapsed but whose refresh cookie may still be
   * good. Returns false when there is nothing to restore, so the caller can show sign-in.
   */
  async restoreSession(): Promise<boolean> {
    if (this.isAuthenticated()) return true
    if (!this.hasSession()) return false

    try {
      await this.refresh()
      return true
    } catch {
      return false
    }
  }

  /**
   * The stored access token, or null when it is absent or lapsed. Synchronous, for deciding
   * what to render; callers that are about to use the token want getValidToken instead.
   */
  getToken(): string | null {
    // A token belongs to a session. If the session is over — an unremembered one whose
    // browsing session ended — the minutes still left on the access token are not the
    // user's to spend, and hasSession has already discarded the record.
    if (!this.hasSession()) return null

    if (this._isExpired()) {
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(TOKEN_EXPIRY_KEY)
      return null
    }
    return localStorage.getItem(TOKEN_KEY)
  }

  isAuthenticated(): boolean {
    return !!this.getToken()
  }

  /**
   * An access token that is good to use right now, renewing first if the stored one is
   * missing or close enough to expiry to be a risk. Raises SessionExpiredError when the
   * session cannot be continued, so callers can send the user to sign in rather than
   * reporting a transport failure.
   */
  async getValidToken(): Promise<string> {
    const token = localStorage.getItem(TOKEN_KEY)
    if (token && !this._isStale()) return token

    // No session was ever established, so there is no credential to renew against; do not
    // make a request that is certain to be refused.
    if (!this.hasSession()) throw new SessionExpiredError()

    // The token just read is the one found wanting, so name it. Nothing observable turns on
    // this today — the read above is the same read the renewal would make — but it keeps the
    // call honest if anything is ever awaited between the two.
    return this.refresh(token)
  }

  /**
   * Exchanges the http-only refresh cookie for a new access token. Concurrent callers share
   * one attempt; the shared promise is cleared from inside it, so a caller arriving after a
   * failure starts a fresh attempt rather than inheriting an abandoned rejection.
   */
  async refresh(supersededToken?: string | null): Promise<string> {
    // Defaulting to what is stored at entry keeps callers that pass nothing behaving as they
    // did: nothing in the store differs from itself, so no reuse is possible and the exchange
    // proceeds.
    const superseded =
      supersededToken !== undefined ? supersededToken : localStorage.getItem(TOKEN_KEY)

    // Another tab of this session may have renewed since the caller last looked. Its token is
    // in the shared store, so exchanging again would be asking for something already had.
    const reusable = this._usableTokenOtherThan(superseded)
    if (reusable) return reusable

    if (this._refreshPromise) return this._refreshPromise

    const generation = ++this._refreshGeneration
    const attempt = (async () => {
      try {
        const response = await fetch('/auth/refresh', {
          method: 'POST',
          // The cookie is the credential; nothing is sent in the body.
          credentials: 'include',
        })

        if (!response.ok) {
          // A refusal may mean only that the credential was superseded while this exchange
          // was in flight. If a sibling stored a token meanwhile, the session is demonstrably
          // alive and discarding it here would sign out every tab of it.
          const evidence = this._tokenOtherThan(superseded)
          if (evidence) {
            // Usable enough to hand back, or only enough to prove the session lives. In the
            // second case the caller gets a failure that leaves the session alone, never one
            // whose handling revokes the credential every other tab depends on.
            const usable = this._usableTokenOtherThan(superseded)
            if (usable) return usable
            throw new RenewalFailedError()
          }

          this.clearLocal()
          throw new SessionExpiredError()
        }

        const dto: TokenDto = await response.json()
        this._store(dto)
        return dto.accessToken
      } finally {
        if (this._refreshGeneration === generation) this._refreshPromise = null
      }
    })()

    this._refreshPromise = attempt
    return attempt
  }

  /**
   * Records the session and its kind. `persistent` is given at authentication, where the
   * user made the choice; a renewal omits it and keeps the kind already recorded, so a
   * renewal can neither promote an ordinary session nor demote a remembered one.
   */
  private _store(dto: TokenDto, persistent?: boolean): void {
    localStorage.setItem(TOKEN_KEY, dto.accessToken)
    localStorage.setItem(TOKEN_EXPIRY_KEY, dto.expiresAt)

    if (persistent !== undefined) {
      localStorage.setItem(SESSION_KEY, persistent ? REMEMBERED : ORDINARY)
    } else if (!localStorage.getItem(SESSION_KEY)) {
      localStorage.setItem(SESSION_KEY, REMEMBERED)
    }

    // Refreshed on every store, so a long-lived tab keeps the beacon alive for as long as
    // the browsing session it belongs to.
    markLive()
  }

  /**
   * A token another client of this session stored, or null. Identity decides whether it is
   * another one: a token can be refused while its expiry still looks good, so a client that
   * compared expiries would present a repudiated token a second time.
   *
   * Absence is not another token. A store that has been emptied means a client signed out,
   * which is the opposite of a sibling having renewed.
   */
  private _tokenOtherThan(superseded: string | null): string | null {
    const stored = localStorage.getItem(TOKEN_KEY)
    if (!stored || stored === superseded) return null
    return stored
  }

  /**
   * The same, narrowed to one worth using. Evidence that a sibling renewed is weaker than a
   * token to renew with: a sibling's token that is itself near expiry proves the session is
   * alive but is not worth adopting, because it would need renewing again immediately.
   */
  private _usableTokenOtherThan(superseded: string | null): string | null {
    const other = this._tokenOtherThan(superseded)
    if (!other || this._isStale()) return null
    return other
  }

  private _expiry(): string | null {
    return localStorage.getItem(TOKEN_EXPIRY_KEY)
  }

  private _isExpired(): boolean {
    const expiry = this._expiry()
    if (!expiry) return false
    return new Date(expiry) <= new Date()
  }

  /** Expired, or close enough to expiry that it should be renewed before use. */
  private _isStale(): boolean {
    const expiry = this._expiry()
    if (!expiry) return false
    return new Date(expiry).getTime() - Date.now() <= RENEWAL_MARGIN_MS
  }
}

export const authService = new AuthService()
