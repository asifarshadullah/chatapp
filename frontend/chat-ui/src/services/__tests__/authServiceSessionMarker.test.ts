import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { authService } from '../authService'

/**
 * Tasks 7.1/7.2 — where the local record of a session lives.
 *
 * The refresh credential is an http-only cookie script cannot read, so the client keeps its
 * own record of whether a session exists. That record has to answer two different questions
 * with one storage: "is this the same browsing session the user signed in during?" (a
 * browser restart discards an unremembered credential) and "is this the same browser?" (a
 * second tab must not be treated as a new visitor).
 *
 * sessionStorage answers the first and fails the second, because it is scoped per tab.
 * So the kind of session lives in localStorage, and a companion browser-session cookie —
 * one the browser discards at exactly the moment it discards the refresh cookie — says
 * whether the browsing session is still the original one.
 */

const FUTURE = () => new Date(Date.now() + 60 * 60_000).toISOString()
const LIVE_COOKIE = 'auth_session_live'

function authResponds() {
  const fetchMock = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ accessToken: 'tok', expiresAt: FUTURE(), userId: 'user-1' }),
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** Discards every browser-session cookie, as closing and reopening the browser does. */
function restartBrowser() {
  document.cookie = `${LIVE_COOKIE}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT`
}

/** A second tab shares cookies and localStorage, and starts with empty sessionStorage. */
function openSecondTab() {
  sessionStorage.clear()
}

function liveCookiePresent() {
  return document.cookie.split(';').some((c) => c.trim().startsWith(`${LIVE_COOKIE}=`))
}

beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  restartBrowser()
  vi.restoreAllMocks()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// ── Task 7.1 — a second tab is not a new visitor ─────────────────────────────

describe('a second tab', () => {
  it('sees an ordinary session', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)

    openSecondTab()

    // The regression this fixes: the second tab used to find nothing and show sign-in.
    expect(authService.hasSession()).toBe(true)
  })

  it('sees a remembered session', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', true)

    openSecondTab()

    expect(authService.hasSession()).toBe(true)
  })

  it('restores an ordinary session rather than showing sign-in', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    openSecondTab()
    localStorage.setItem('auth_token_expiry', new Date(Date.now() - 1000).toISOString())
    const fetchMock = authResponds()

    const restored = await authService.restoreSession()

    expect(restored).toBe(true)
    expect(fetchMock).toHaveBeenCalled()
  })
})

// ── Task 7.1 — the kind is recorded, in localStorage ─────────────────────────

describe('the recorded kind', () => {
  it('records an ordinary session as ordinary', async () => {
    authResponds()

    await authService.login('user@test.com', 'Password123', false)

    expect(localStorage.getItem('auth_session')).toBe('ordinary')
    expect(sessionStorage.getItem('auth_session')).toBeNull()
  })

  it('records a remembered session as remembered', async () => {
    authResponds()

    await authService.login('user@test.com', 'Password123', true)

    expect(localStorage.getItem('auth_session')).toBe('remembered')
  })

  it('marks an ordinary session live with a browser-session cookie', async () => {
    authResponds()

    await authService.login('user@test.com', 'Password123', false)

    expect(liveCookiePresent()).toBe(true)
  })

  it('keeps the kind across a renewal', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    authResponds()

    await authService.refresh()

    expect(localStorage.getItem('auth_session')).toBe('ordinary')
  })
})

// ── Task 7.2 — a browser restart, without a doomed request ───────────────────

describe('after a browser restart', () => {
  it('does not see an ordinary session', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)

    restartBrowser()
    openSecondTab()

    expect(authService.hasSession()).toBe(false)
  })

  it('attempts no renewal for an ordinary session', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    restartBrowser()
    openSecondTab()
    const fetchMock = authResponds()

    const restored = await authService.restoreSession()

    // The credential went with the browsing session; a request would only be refused,
    // and would flash the app shell on the way to the sign-in form.
    expect(restored).toBe(false)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('clears the stale record for an ordinary session', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    restartBrowser()
    openSecondTab()

    authService.hasSession()

    expect(localStorage.getItem('auth_session')).toBeNull()
    expect(localStorage.getItem('auth_token')).toBeNull()
  })

  it('still sees a remembered session and renews it', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', true)
    restartBrowser()
    openSecondTab()
    localStorage.setItem('auth_token_expiry', new Date(Date.now() - 1000).toISOString())
    const fetchMock = authResponds()

    const restored = await authService.restoreSession()

    expect(restored).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith('/auth/refresh', expect.objectContaining({
      method: 'POST',
    }))
  })
})

// ── Ending a session ─────────────────────────────────────────────────────────

describe('ending a session', () => {
  it('clears the record and the live cookie on logout', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true }))

    await authService.logout()

    expect(localStorage.getItem('auth_session')).toBeNull()
    expect(liveCookiePresent()).toBe(false)
    expect(authService.hasSession()).toBe(false)
  })

  it('treats a session recorded before the kind existed as remembered', async () => {
    // A user signed in under the previous release, whose marker is the old 'active'.
    // Renewing may fail, but that is honest; refusing to try would sign them out for a
    // deploy they had nothing to do with.
    localStorage.setItem('auth_token', 'stored')
    localStorage.setItem('auth_token_expiry', FUTURE())
    localStorage.setItem('auth_session', 'active')

    expect(authService.hasSession()).toBe(true)
  })
})
