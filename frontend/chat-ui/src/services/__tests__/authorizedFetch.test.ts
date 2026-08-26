import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { authorizedFetch } from '../authorizedFetch'
import { SessionExpiredError } from '../sessionErrors'

/**
 * Every authenticated request renews when needed and retries once if the server still
 * refuses. Without this the chat hub renews silently while ordinary API calls keep sending
 * a lapsed token, so the conversation survives an hour but "Manage Plan" does not.
 */

const FUTURE = () => new Date(Date.now() + 60 * 60_000).toISOString()
const NEARLY_STALE = () => new Date(Date.now() + 30_000).toISOString()

function signedIn(expiry: string = FUTURE(), token = 'stored-token') {
  localStorage.setItem('auth_token', token)
  localStorage.setItem('auth_token_expiry', expiry)
  localStorage.setItem('auth_session', 'active')
}

function renewalReturns(token: string) {
  return {
    ok: true,
    status: 200,
    json: async () => ({ accessToken: token, expiresAt: FUTURE(), userId: 'u' }),
  }
}

function bearerOf(call: unknown[]): string | undefined {
  const init = call[1] as RequestInit | undefined
  return (init?.headers as Record<string, string>)?.Authorization
}

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

afterEach(() => vi.unstubAllGlobals())

describe('authorizedFetch', () => {
  it('sends the stored token when it is fresh', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    await authorizedFetch('/api/chat')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(bearerOf(fetchMock.mock.calls[0])).toBe('Bearer stored-token')
  })

  it('renews before the request when the token is stale', async () => {
    signedIn(NEARLY_STALE())
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(renewalReturns('renewed-token'))
      .mockResolvedValueOnce({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    await authorizedFetch('/api/chat')

    expect(fetchMock.mock.calls[0][0]).toBe('/auth/refresh')
    expect(bearerOf(fetchMock.mock.calls[1])).toBe('Bearer renewed-token')
  })

  it('preserves the caller headers and method', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    await authorizedFetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{"message":"hi"}',
    })

    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
    expect(init.body).toBe('{"message":"hi"}')
  })

  it('retries once with a renewed token when the server refuses', async () => {
    signedIn()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: false, status: 401 })
      .mockResolvedValueOnce(renewalReturns('renewed-token'))
      .mockResolvedValueOnce({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    const response = await authorizedFetch('/api/chat')

    expect(response.status).toBe(200)
    expect(fetchMock.mock.calls[1][0]).toBe('/auth/refresh')
    expect(bearerOf(fetchMock.mock.calls[2])).toBe('Bearer renewed-token')
  })

  it('retries only once, so a persistent 401 does not loop', async () => {
    signedIn()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: false, status: 401 })
      .mockResolvedValueOnce(renewalReturns('renewed-token'))
      .mockResolvedValueOnce({ ok: false, status: 401 })
    vi.stubGlobal('fetch', fetchMock)

    const response = await authorizedFetch('/api/chat')

    expect(response.status).toBe(401)
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('raises an ended session when the retry cannot be renewed', async () => {
    signedIn()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: false, status: 401 })
      .mockResolvedValueOnce({ ok: false, status: 401 }) // the renewal itself is refused
    vi.stubGlobal('fetch', fetchMock)

    await expect(authorizedFetch('/api/chat')).rejects.toBeInstanceOf(SessionExpiredError)
  })

  it('does not retry a failure that is not an authorization failure', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, status: 500 })
    vi.stubGlobal('fetch', fetchMock)

    const response = await authorizedFetch('/api/chat')

    expect(response.status).toBe(500)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('raises an ended session when there is no session at all', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    await expect(authorizedFetch('/api/chat')).rejects.toBeInstanceOf(SessionExpiredError)
    expect(fetchMock).not.toHaveBeenCalled()
  })
})
