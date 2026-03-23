import { describe, it, expect, vi, beforeEach } from 'vitest'
import { authService } from '../authService'

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

// ── Cycle FA1.1 ──────────────────────────────────────────────────────────────

describe('login', () => {
  it('stores token and returns TokenDto on success', async () => {
    const mockResponse = {
      accessToken: 'test-jwt-token',
      expiresAt: '2026-03-24T00:00:00Z',
      userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    }))

    const result = await authService.login('user@test.com', 'Password123')

    expect(result.accessToken).toBe('test-jwt-token')
    expect(authService.getToken()).toBe('test-jwt-token')
  })

  it('sends POST to /auth/login with correct body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ accessToken: 'tok', expiresAt: '', userId: '' }),
    }))

    await authService.login('user@test.com', 'Password123')

    expect(fetch).toHaveBeenCalledWith('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'user@test.com', password: 'Password123' }),
    })
  })

  it('throws on non-ok response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401 }))

    await expect(authService.login('bad@test.com', 'wrong')).rejects.toThrow('401')
  })
})

// ── Cycle FA1.2 ──────────────────────────────────────────────────────────────

describe('register', () => {
  it('stores token and returns TokenDto on success', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ accessToken: 'reg-token', expiresAt: '', userId: 'uid' }),
    }))

    const result = await authService.register('new@test.com', 'Password123', 'Alice')

    expect(result.accessToken).toBe('reg-token')
    expect(authService.getToken()).toBe('reg-token')
  })

  it('sends POST to /auth/register with correct body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ accessToken: 'tok', expiresAt: '', userId: '' }),
    }))

    await authService.register('new@test.com', 'Password123', 'Alice')

    expect(fetch).toHaveBeenCalledWith('/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'new@test.com', password: 'Password123', displayName: 'Alice' }),
    })
  })
})

// ── Cycle FA1.3 ──────────────────────────────────────────────────────────────

describe('logout', () => {
  it('clears the stored token', () => {
    localStorage.setItem('auth_token', 'some-token')

    authService.logout()

    expect(authService.getToken()).toBeNull()
  })
})

// ── Cycle FA1.4 ──────────────────────────────────────────────────────────────

describe('isAuthenticated', () => {
  it('returns true when token exists', () => {
    localStorage.setItem('auth_token', 'some-token')
    expect(authService.isAuthenticated()).toBe(true)
  })

  it('returns false when no token', () => {
    expect(authService.isAuthenticated()).toBe(false)
  })
})

// ── Token expiry ──────────────────────────────────────────────────────────────

describe('token expiry', () => {
  it('getToken returns null and clears storage when token is expired', () => {
    localStorage.setItem('auth_token', 'expired-token')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() - 1000).toISOString())

    expect(authService.getToken()).toBeNull()
    expect(localStorage.getItem('auth_token')).toBeNull()
  })

  it('getToken returns token when not yet expired', () => {
    localStorage.setItem('auth_token', 'valid-token')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() + 60_000).toISOString())

    expect(authService.getToken()).toBe('valid-token')
  })

  it('isAuthenticated returns false when token is expired', () => {
    localStorage.setItem('auth_token', 'expired-token')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() - 1000).toISOString())

    expect(authService.isAuthenticated()).toBe(false)
  })
})
