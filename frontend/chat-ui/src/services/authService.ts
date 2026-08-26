import { SessionExpiredError } from './sessionErrors'

export interface TokenDto {
  accessToken: string
  expiresAt: string
  userId: string
}

const TOKEN_KEY = 'auth_token'
const TOKEN_EXPIRY_KEY = 'auth_token_expiry'

/**
 * Marks that a session was established. The refresh credential is an http-only cookie that
 * script cannot see, so without this the client cannot tell "never signed in" from "access
 * token expired and was cleared" — and would show the sign-in form to someone whose refresh
 * cookie is still perfectly good.
 */
const SESSION_KEY = 'auth_session'

/**
 * How close to expiry an access token may get before it is renewed. A margin rather than
 * zero so renewal happens before a request can fail, and wide enough to absorb clock skew
 * between the browser and the API.
 *
 * Access tokens are short-lived by design — see Jwt.ExpiryMinutes — so this stays well under
 * that lifetime: a margin approaching it would renew almost every request.
 */
const RENEWAL_MARGIN_MS = 60_000

class AuthService {
  /** The one renewal in flight, shared by every concurrent caller. */
  private _refreshPromise: Promise<string> | null = null
  private _refreshGeneration = 0

  async login(email: string, password: string): Promise<TokenDto> {
    const response = await fetch('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // The refresh credential comes back as an http-only cookie, which the browser only
      // stores when the request is made with credentials.
      credentials: 'include',
      body: JSON.stringify({ email, password }),
    })

    if (!response.ok) {
      throw new Error(`${response.status}`)
    }

    const dto: TokenDto = await response.json()
    this._store(dto)
    return dto
  }

  async register(email: string, password: string, displayName: string): Promise<TokenDto> {
    const response = await fetch('/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ email, password, displayName }),
    })

    if (!response.ok) {
      throw new Error(`${response.status}`)
    }

    const dto: TokenDto = await response.json()
    this._store(dto)
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
  }

  /**
   * Whether a session was established and has not been ended. True while the refresh cookie
   * is expected to still exist, even once the access token has lapsed.
   */
  hasSession(): boolean {
    return localStorage.getItem(SESSION_KEY) === 'active'
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

    return this.refresh()
  }

  /**
   * Exchanges the http-only refresh cookie for a new access token. Concurrent callers share
   * one attempt; the shared promise is cleared from inside it, so a caller arriving after a
   * failure starts a fresh attempt rather than inheriting an abandoned rejection.
   */
  async refresh(): Promise<string> {
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

  private _store(dto: TokenDto): void {
    localStorage.setItem(TOKEN_KEY, dto.accessToken)
    localStorage.setItem(TOKEN_EXPIRY_KEY, dto.expiresAt)
    localStorage.setItem(SESSION_KEY, 'active')
  }

  private _isExpired(): boolean {
    const expiry = localStorage.getItem(TOKEN_EXPIRY_KEY)
    if (!expiry) return false
    return new Date(expiry) <= new Date()
  }

  /** Expired, or close enough to expiry that it should be renewed before use. */
  private _isStale(): boolean {
    const expiry = localStorage.getItem(TOKEN_EXPIRY_KEY)
    if (!expiry) return false
    return new Date(expiry).getTime() - Date.now() <= RENEWAL_MARGIN_MS
  }
}

export const authService = new AuthService()
