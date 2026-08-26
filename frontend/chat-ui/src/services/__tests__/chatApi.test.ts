import { describe, it, expect, vi, beforeEach } from 'vitest'
import { sendMessage } from '../chatApi'

describe('sendMessage', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  // These endpoints require a bearer token, and the client now renews before sending, so a
  // signed-in session has to exist for the request to be attempted at all.
  beforeEach(() => {
    localStorage.setItem('auth_token', 'test-token')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() + 3_600_000).toISOString())
    localStorage.setItem('auth_session', 'active')
  })

  // Cycle 3.2 — success response
  it('returns parsed ChatResponse on success', async () => {
    const mockResponse = { id: 'abc', message: 'Echo: hi', role: 'assistant', timestamp: '2024-01-01T00:00:00Z' }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse,
    }))

    const result = await sendMessage('hi')

    expect(result).toEqual(mockResponse)
  })

  // Cycle 3.4 — network failure
  it('throws error on network failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('Network error')))

    await expect(sendMessage('hi')).rejects.toThrow('Network error')
  })

  // Cycle 3.3 — HTTP error
  it('throws error on non-ok response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({}),
    }))

    await expect(sendMessage('hi')).rejects.toThrow('API error: 500')
  })

  // Cycle 3.1 — POST body
  it('sends POST request with correct body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ id: '1', message: 'Echo: hi', role: 'assistant', timestamp: '' }),
    }))

    await sendMessage('hi')

    // The bearer token is attached by authorizedFetch, which also renews it when stale.
    expect(fetch).toHaveBeenCalledWith('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer test-token' },
      body: JSON.stringify({ message: 'hi' }),
    })
  })
})
