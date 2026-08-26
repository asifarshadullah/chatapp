import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { authService } from '../authService'
import { SessionExpiredError } from '../sessionErrors'

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
