import { describe, it, expect, vi, beforeEach } from 'vitest'
import { billingService } from '../billingService'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const TOKEN = 'test-token'
vi.mock('../authService', () => ({
  authService: { getToken: () => TOKEN },
}))

beforeEach(() => {
  mockFetch.mockReset()
})

// ── FB1.1: getPlans ───────────────────────────────────────────────────────────

describe('getPlans', () => {
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
    const status = { planName: 'Pro', tier: 'Pro', status: 'Active', currentPeriodEnd: '2025-12-31T00:00:00Z' }
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => status })

    const result = await billingService.getSubscription()

    expect(mockFetch).toHaveBeenCalledWith('/billing/subscription', {
      headers: { Authorization: `Bearer ${TOKEN}` },
    })
    expect(result).toEqual(status)
  })

  it('getSubscription_returnsNull_when404', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 404 })

    const result = await billingService.getSubscription()

    expect(result).toBeNull()
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
