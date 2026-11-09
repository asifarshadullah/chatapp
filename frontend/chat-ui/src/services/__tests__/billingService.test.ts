import { describe, it, expect, vi, beforeEach } from 'vitest'
import { billingService, UpgradesUnavailableError } from '../billingService'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const TOKEN = 'test-token'
vi.mock('../authService', () => ({
  authService: {
    getToken: () => TOKEN,
    // Requests now renew before sending, so the mock must serve the renewing accessor too.
    getValidToken: async () => TOKEN,
    refresh: async () => TOKEN,
  },
}))

beforeEach(() => {
  mockFetch.mockReset()
})

// ── FB1.1: getPlans ───────────────────────────────────────────────────────────

describe('getPlans', () => {

  // These endpoints require a bearer token, and the client now renews before sending, so a
  // signed-in session has to exist for the request to be attempted at all.
  beforeEach(() => {
    localStorage.setItem('auth_token', 'test-token')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() + 3_600_000).toISOString())
    localStorage.setItem('auth_session', 'active')
  })

  it('getPlans_returnsPlansArray', async () => {
    const plans = [
      { id: '1', name: 'Free', tier: 'Free', pricePerMonth: 0, features: ['chat'] },
      { id: '2', name: 'Pro', tier: 'Pro', pricePerMonth: 19, features: ['chat', 'upload'] },
    ]
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => plans })

    const result = await billingService.getPlans()

    expect(mockFetch).toHaveBeenCalledWith('/billing/plans', {
      headers: { Authorization: `Bearer ${TOKEN}` },
    })
    expect(result).toEqual(plans)
  })
})

// ── FB1.2: subscribe ──────────────────────────────────────────────────────────

describe('subscribe', () => {
  it('subscribe_whenUpgradesAreOff_raisesTheServersMessage', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 503, text: async () => 'Upgrades are off.' })

    const failure = billingService.subscribe('plan-id-pro')

    await expect(failure).rejects.toBeInstanceOf(UpgradesUnavailableError)
    await expect(failure).rejects.toThrow('Upgrades are off.')
  })

  it('subscribe_whenAnythingElseFails_raisesAnOrdinaryError', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500 })

    const failure = billingService.subscribe('plan-id-pro')

    await expect(failure).rejects.toThrow('500')
    await expect(failure).rejects.not.toBeInstanceOf(UpgradesUnavailableError)
  })

  it('subscribe_returnsCheckoutUrl', async () => {
    const dto = { checkoutUrl: 'https://checkout.stripe.com/pay/cs_test_123', sessionId: 'cs_test_123' }
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => dto })

    const result = await billingService.subscribe('plan-id-pro')

    expect(mockFetch).toHaveBeenCalledWith('/billing/subscribe', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${TOKEN}` },
      body: JSON.stringify({ planId: 'plan-id-pro' }),
    })
    expect(result).toEqual(dto)
  })
})

// ── FB1.3: getSubscription ────────────────────────────────────────────────────

describe('getSubscription', () => {
  it('getSubscription_returnsStatus', async () => {
    const status = {
      planName: 'Pro',
      tier: 'Pro',
      status: 'Active',
      currentPeriodEnd: '2025-12-31T00:00:00Z',
      cancelAtPeriodEnd: false,
    }
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => status })

    const result = await billingService.getSubscription()

    expect(mockFetch).toHaveBeenCalledWith('/billing/subscription', {
      headers: { Authorization: `Bearer ${TOKEN}` },
    })
    expect(result).toEqual(status)
  })

  it('getSubscription_returnsFree_forAUserWithoutAPaidPlan', async () => {
    const free = { planName: 'Free', tier: 'Free', status: null, currentPeriodEnd: null, cancelAtPeriodEnd: false }
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => free })

    expect(await billingService.getSubscription()).toEqual(free)
  })

  it('getSubscription_throws_whenTheRequestFails', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500 })

    await expect(billingService.getSubscription()).rejects.toThrow('500')
  })
})

// ── The checkout mode ─────────────────────────────────────────────────────────

describe('getCheckoutMode', () => {
  it('getCheckoutMode_returnsTheMode', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ mode: 'test' }) })

    const mode = await billingService.getCheckoutMode()

    expect(mockFetch).toHaveBeenCalledWith('/billing/checkout-mode', {
      headers: { Authorization: `Bearer ${TOKEN}` },
    })
    expect(mode).toBe('test')
  })

  it('getCheckoutMode_throws_whenTheRequestFails', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500 })

    await expect(billingService.getCheckoutMode()).rejects.toThrow('500')
  })
})

// ── FB1.4: cancelSubscription ─────────────────────────────────────────────────

describe('cancelSubscription', () => {
  it('cancelSubscription_callsDeleteEndpoint', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true })

    await billingService.cancelSubscription()

    expect(mockFetch).toHaveBeenCalledWith('/billing/subscription', {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${TOKEN}` },
    })
  })
})
