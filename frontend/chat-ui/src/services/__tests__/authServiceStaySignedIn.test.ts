import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { authService } from '../authService'

/**
 * Tasks 5.1–5.3 — the "keep me signed in" choice on the client side.
 *
 * The refresh credential is an http-only cookie that script cannot see, so what is testable
 * here is the choice being sent, and the local record of the session living in a storage
 * that matches how long the cookie is meant to last: sessionStorage for a session that ends
 * with the browser, localStorage for one that does not.
 */

const FUTURE = () => new Date(Date.now() + 60 * 60_000).toISOString()

function authResponds() {
  const fetchMock = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ accessToken: 'tok', expiresAt: FUTURE(), userId: 'user-1' }),
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function bodyOf(fetchMock: ReturnType<typeof vi.fn>, call = 0) {
  return JSON.parse(fetchMock.mock.calls[call][1].body)
}

/** Simulates closing and reopening the browser: browser-session cookies are discarded. */
function restartBrowser() {
  document.cookie = 'auth_session_live=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT'
  sessionStorage.clear()
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

// ── Task 5.1 — the choice is sent ────────────────────────────────────────────

describe('sending the choice', () => {
  it('sends staySignedIn on login when chosen', async () => {
    const fetchMock = authResponds()

    await authService.login('user@test.com', 'Password123', true)

    expect(bodyOf(fetchMock)).toEqual({
      email: 'user@test.com',
      password: 'Password123',
      staySignedIn: true,
    })
  })

  it('sends staySignedIn false when not chosen', async () => {
    const fetchMock = authResponds()

    await authService.login('user@test.com', 'Password123', false)

    expect(bodyOf(fetchMock).staySignedIn).toBe(false)
  })

  it('defaults to not staying signed in', async () => {
    const fetchMock = authResponds()

    await authService.login('user@test.com', 'Password123')

    expect(bodyOf(fetchMock).staySignedIn).toBe(false)
  })

  it('sends staySignedIn on register', async () => {
    const fetchMock = authResponds()

    await authService.register('new@test.com', 'Password123', 'New', true)

    expect(bodyOf(fetchMock)).toEqual({
      email: 'new@test.com',
      password: 'Password123',
      displayName: 'New',
      staySignedIn: true,
    })
  })
})

// ── Task 5.2 — the session record ───────────────────────────────────────────
//
// Where the record lives, and how a browser restart is told from a second tab, is covered
// in authServiceSessionMarker.test.ts. What matters here is only that the choice the user
// made is the one that gets recorded.

describe('what gets recorded', () => {
  it('records a remembered session as remembered', async () => {
    authResponds()

    await authService.login('user@test.com', 'Password123', true)

    expect(localStorage.getItem('auth_session')).toBe('remembered')
  })

  it('records an ordinary session as ordinary', async () => {
    authResponds()

    await authService.login('user@test.com', 'Password123', false)

    expect(localStorage.getItem('auth_session')).toBe('ordinary')
  })

  it('keeps a remembered session remembered across a renewal', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', true)
    authResponds()

    await authService.refresh()

    expect(localStorage.getItem('auth_session')).toBe('remembered')
  })

  it('does not promote an ordinary session on renewal', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    authResponds()

    await authService.refresh()

    expect(localStorage.getItem('auth_session')).toBe('ordinary')
  })
})

// ── Task 5.3 — ending a session clears both records ─────────────────────────

describe('ending a session', () => {
  it('clears a remembered session on logout', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', true)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true }))

    await authService.logout()

    expect(localStorage.getItem('auth_session')).toBeNull()
    expect(localStorage.getItem('auth_token')).toBeNull()
    expect(authService.hasSession()).toBe(false)
  })

  it('clears an ordinary session on logout', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', false)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true }))

    await authService.logout()

    expect(localStorage.getItem('auth_session')).toBeNull()
    expect(localStorage.getItem('auth_token')).toBeNull()
    expect(authService.hasSession()).toBe(false)
  })

  it('clears a remembered session when renewal is refused', async () => {
    authResponds()
    await authService.login('user@test.com', 'Password123', true)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401 }))

    await expect(authService.refresh()).rejects.toThrow()

    expect(localStorage.getItem('auth_session')).toBeNull()
    expect(authService.hasSession()).toBe(false)
  })
})
