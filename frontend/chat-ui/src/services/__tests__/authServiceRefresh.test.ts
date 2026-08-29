import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { authService } from '../authService'
import { RenewalFailedError, SessionExpiredError } from '../sessionErrors'

/**
 * Silent renewal. The refresh credential itself is an http-only cookie the browser attaches,
 * so nothing here reads or sends it — what is tested is that the client renews at the right
 * moments, shares one renewal between concurrent callers, and ends the session honestly when
 * renewal is refused.
 */

const FUTURE = () => new Date(Date.now() + 60 * 60_000).toISOString()
const NEARLY_STALE = () => new Date(Date.now() + 30_000).toISOString()
const PAST = () => new Date(Date.now() - 1_000).toISOString()

function signedIn(expiry: string = FUTURE()) {
  localStorage.setItem('auth_token', 'stored-token')
  localStorage.setItem('auth_token_expiry', expiry)
  localStorage.setItem('auth_session', 'active')
}

function refreshResponds(token = 'renewed-token', expiresAt = FUTURE()) {
  const fetchMock = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ accessToken: token, expiresAt, userId: 'user-1' }),
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function refreshRefused(status = 401) {
  const fetchMock = vi.fn().mockResolvedValue({ ok: false, status })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// ── Task 6.1 — the refresh call itself ───────────────────────────────────────

describe('refresh', () => {
  it('posts to /auth/refresh and stores the renewed token', async () => {
    signedIn()
    const fetchMock = refreshResponds('renewed-token')

    const token = await authService.refresh()

    expect(fetchMock).toHaveBeenCalledWith('/auth/refresh', expect.objectContaining({
      method: 'POST',
    }))
    expect(token).toBe('renewed-token')
    expect(localStorage.getItem('auth_token')).toBe('renewed-token')
  })

  it('sends the request with credentials so the http-only cookie is attached', async () => {
    signedIn()
    const fetchMock = refreshResponds()

    await authService.refresh()

    // Without this the browser withholds the cookie and every renewal fails.
    const [, init] = fetchMock.mock.calls[0]
    expect(init.credentials).toBe('include')
  })

  it('never sends a refresh token in the body — the cookie carries it', async () => {
    signedIn()
    const fetchMock = refreshResponds()

    await authService.refresh()

    const [, init] = fetchMock.mock.calls[0]
    expect(init.body ?? '').not.toContain('refresh')
  })

  it('raises SessionExpiredError when renewal is refused', async () => {
    signedIn()
    refreshRefused(401)

    await expect(authService.refresh()).rejects.toBeInstanceOf(SessionExpiredError)
  })

  it('clears stored credentials when renewal is refused', async () => {
    signedIn()
    refreshRefused(401)

    await authService.refresh().catch(() => {})

    expect(localStorage.getItem('auth_token')).toBeNull()
  })
})

// ── Task 6.2 — single flight ─────────────────────────────────────────────────

describe('concurrent renewal', () => {
  it('shares one in-flight refresh between concurrent callers', async () => {
    signedIn()
    const fetchMock = refreshResponds()

    const [a, b, c] = await Promise.all([
      authService.refresh(),
      authService.refresh(),
      authService.refresh(),
    ])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect([a, b, c]).toEqual(['renewed-token', 'renewed-token', 'renewed-token'])
  })

  it('gives a later caller a fresh attempt after a failed refresh', async () => {
    signedIn()
    refreshRefused(401)
    await authService.refresh().catch(() => {})

    // The same failure mode fixed in signalRService: a caller arriving after a failure must
    // not be handed the abandoned rejection.
    signedIn()
    const fetchMock = refreshResponds('second-attempt')
    const token = await authService.refresh()

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(token).toBe('second-attempt')
  })
})

// ── Task 6.3 / 6.6 — when renewal happens ────────────────────────────────────

describe('getValidToken', () => {
  it('returns the stored token without renewing when it is fresh', async () => {
    signedIn(FUTURE())
    const fetchMock = refreshResponds()

    const token = await authService.getValidToken()

    expect(token).toBe('stored-token')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('renews when the token is within the staleness margin', async () => {
    signedIn(NEARLY_STALE())
    const fetchMock = refreshResponds('renewed-token')

    const token = await authService.getValidToken()

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(token).toBe('renewed-token')
  })

  it('renews when the token has already lapsed', async () => {
    signedIn(PAST())
    refreshResponds('renewed-token')

    await expect(authService.getValidToken()).resolves.toBe('renewed-token')
  })

  it('raises SessionExpiredError when renewal is refused', async () => {
    signedIn(PAST())
    refreshRefused(401)

    await expect(authService.getValidToken()).rejects.toBeInstanceOf(SessionExpiredError)
  })

  it('does not attempt to renew when no session exists', async () => {
    const fetchMock = refreshResponds()

    await expect(authService.getValidToken()).rejects.toBeInstanceOf(SessionExpiredError)
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

// ── Sign-out reaches the server ──────────────────────────────────────────────

describe('logout', () => {
  it('tells the server to revoke the refresh credential', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 })
    vi.stubGlobal('fetch', fetchMock)

    await authService.logout()

    expect(fetchMock).toHaveBeenCalledWith('/auth/logout', expect.objectContaining({
      method: 'POST',
      credentials: 'include',
    }))
    expect(localStorage.getItem('auth_token')).toBeNull()
  })

  it('clears local state even when the server call fails', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    await authService.logout()

    // Being unable to reach the server must not leave the user apparently signed in.
    expect(localStorage.getItem('auth_token')).toBeNull()
  })
})

// ── Restoring a session whose access token has already lapsed ────────────────

describe('restoreSession', () => {
  it('renews when the access token has expired but a session was established', async () => {
    signedIn(PAST())
    localStorage.setItem('auth_session', 'active')
    const fetchMock = refreshResponds('restored-token')

    // Reading the token first is what clears it, reproducing a page load.
    expect(authService.isAuthenticated()).toBe(false)

    await expect(authService.restoreSession()).resolves.toBe(true)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(authService.getToken()).toBe('restored-token')
  })

  it('does not renew when no session was ever established', async () => {
    const fetchMock = refreshResponds()

    await expect(authService.restoreSession()).resolves.toBe(false)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('reports failure when the refresh credential is no longer good', async () => {
    signedIn(PAST())
    localStorage.setItem('auth_session', 'active')
    refreshRefused(401)

    await expect(authService.restoreSession()).resolves.toBe(false)
    expect(authService.hasSession()).toBe(false)
  })

  it('does not renew when the stored token is still fresh', async () => {
    signedIn(FUTURE())
    localStorage.setItem('auth_session', 'active')
    const fetchMock = refreshResponds()

    await expect(authService.restoreSession()).resolves.toBe(true)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('logout ends the session so a reload does not try to restore it', async () => {
    signedIn()
    localStorage.setItem('auth_session', 'active')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 204 }))

    await authService.logout()

    expect(authService.hasSession()).toBe(false)
  })
})

// ── Sharing a renewal across tabs ────────────────────────────────────────────

/**
 * Every tab of the origin shares one localStorage, so a token one tab obtains is sitting
 * there for the others. "Another tab renewed" is staged as a write to that shared store —
 * for the in-flight cases, from inside the mocked fetch, between the exchange being issued
 * and its outcome arriving.
 */
function siblingStored(token: string, expiry: string = FUTURE()) {
  localStorage.setItem('auth_token', token)
  localStorage.setItem('auth_token_expiry', expiry)
}

describe('reusing a renewal a sibling obtained', () => {
  it('returns a sibling token instead of exchanging', async () => {
    signedIn()
    const fetchMock = refreshResponds('renewed-token')
    siblingStored('sibling-token')

    const token = await authService.refresh('stored-token')

    expect(token).toBe('sibling-token')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('exchanges rather than adopt a sibling token that is itself stale', async () => {
    signedIn()
    const fetchMock = refreshResponds('renewed-token')
    // A sibling did renew, but its token is already inside the renewal margin: adopting it
    // would mean renewing again moments later.
    siblingStored('sibling-token', NEARLY_STALE())

    const token = await authService.refresh('stored-token')

    expect(token).toBe('renewed-token')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})

describe('a refusal that another tab has already overtaken', () => {
  /** Refuses the exchange, but only after a sibling has stored a token of its own. */
  function refusedAfterSiblingRenewed(token: string, expiry: string = FUTURE()) {
    const fetchMock = vi.fn().mockImplementation(async () => {
      siblingStored(token, expiry)
      return { ok: false, status: 401 }
    })
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  it('keeps the session and continues on the token the sibling stored', async () => {
    signedIn()
    refusedAfterSiblingRenewed('sibling-token')

    const token = await authService.refresh('stored-token')

    expect(token).toBe('sibling-token')
    // The credential was superseded, not repudiated. Discarding the session here is what
    // signs out every other tab.
    expect(localStorage.getItem('auth_token')).toBe('sibling-token')
    expect(localStorage.getItem('auth_session')).toBe('active')
  })

  it('spares the session when the sibling token is evidence but not usable', async () => {
    signedIn()
    // Nearly stale: proof a sibling exchanged successfully a moment ago, yet not worth
    // adopting. Evidence the session lives is a weaker thing than a token to use, and
    // conflating them is what ends a session that is demonstrably alive.
    refusedAfterSiblingRenewed('sibling-token', NEARLY_STALE())

    await expect(authService.refresh('stored-token')).rejects.toBeInstanceOf(RenewalFailedError)

    expect(localStorage.getItem('auth_token')).toBe('sibling-token')
    expect(localStorage.getItem('auth_session')).toBe('active')
  })

  it('does not report a failed renewal as an ended session', async () => {
    signedIn()
    refusedAfterSiblingRenewed('sibling-token', NEARLY_STALE())

    // SessionExpiredError is acted on: its handler revokes the credential family server-side,
    // which would end the session for every other tab.
    await expect(authService.refresh('stored-token')).rejects.not.toBeInstanceOf(
      SessionExpiredError,
    )
  })

  it('ends the session when a refusal has nothing behind it', async () => {
    signedIn()
    refreshRefused()

    await expect(authService.refresh('stored-token')).rejects.toBeInstanceOf(SessionExpiredError)

    expect(localStorage.getItem('auth_token')).toBeNull()
    expect(localStorage.getItem('auth_session')).toBeNull()
  })

  it('treats a store emptied by a sign-out elsewhere as the session ending', async () => {
    signedIn()
    // Another tab signed out while this exchange was in flight. The stored token is now
    // absent, which is certainly "different" — and means the opposite of a sibling renewing.
    const fetchMock = vi.fn().mockImplementation(async () => {
      authService.clearLocal()
      return { ok: false, status: 401 }
    })
    vi.stubGlobal('fetch', fetchMock)

    await expect(authService.refresh('stored-token')).rejects.toBeInstanceOf(SessionExpiredError)

    expect(localStorage.getItem('auth_token')).toBeNull()
  })

  it('stops a revoked session once the sibling token it coasted on lapses', async () => {
    signedIn()
    refusedAfterSiblingRenewed('sibling-token')
    await authService.refresh('stored-token')

    // The session was revoked, so nothing further is ever stored. When the adopted token
    // lapses, the next renewal is refused with no evidence behind it.
    localStorage.setItem('auth_token_expiry', PAST())
    refreshRefused()

    await expect(authService.refresh('sibling-token')).rejects.toBeInstanceOf(SessionExpiredError)
    expect(localStorage.getItem('auth_token')).toBeNull()
  })
})
