import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { BillingPage } from '../BillingPage'

const { mockGetPlans, mockSubscribe, mockGetSubscription, mockCancelSubscription } = vi.hoisted(() => ({
  mockGetPlans: vi.fn(),
  mockSubscribe: vi.fn(),
  mockGetSubscription: vi.fn(),
  mockCancelSubscription: vi.fn(),
}))

vi.mock('../../services/billingService', () => ({
  billingService: {
    getPlans: mockGetPlans,
    subscribe: mockSubscribe,
    getSubscription: mockGetSubscription,
    cancelSubscription: mockCancelSubscription,
  },
}))

const plans = [
  { id: 'free-id', name: 'Free', tier: 'Free', pricePerMonth: 0, features: ['Chat'] },
  { id: 'pro-id', name: 'Pro', tier: 'Pro', pricePerMonth: 19, features: ['Chat', 'Document Upload'] },
  { id: 'ent-id', name: 'Enterprise', tier: 'Enterprise', pricePerMonth: 99, features: ['Chat', 'Document Upload', 'Share'] },
]

beforeEach(() => {
  vi.resetAllMocks()
  mockGetPlans.mockResolvedValue(plans)
  mockGetSubscription.mockResolvedValue(null)
  Object.defineProperty(window, 'location', {
    value: { href: '' },
    writable: true,
    configurable: true,
  })
})

// ── FB2.1: renders plan cards ─────────────────────────────────────────────────

describe('BillingPage', () => {
  it('renders_planCards_withNamePriceAndFeatures', async () => {
    render(<BillingPage onBack={vi.fn()} />)

    await waitFor(() => {
      expect(screen.getByText('Free')).toBeDefined()
      expect(screen.getByText('Pro')).toBeDefined()
      expect(screen.getByText('Enterprise')).toBeDefined()
    })

    expect(screen.getAllByText(/\$0/).length).toBeGreaterThan(0)
    expect(screen.getByText(/\$19/)).toBeDefined()
    expect(screen.getByText(/\$99/)).toBeDefined()
    expect(screen.getAllByText(/Document Upload/).length).toBeGreaterThan(0)
  })

  // ── FB2.2: subscribe redirects ─────────────────────────────────────────────

  it('subscribe_button_callsBillingService_andRedirectsToCheckoutUrl', async () => {
    mockSubscribe.mockResolvedValueOnce({
      checkoutUrl: 'https://checkout.stripe.com/pay/cs_test_abc',
      sessionId: 'cs_test_abc',
    })

    render(<BillingPage onBack={vi.fn()} />)
    await waitFor(() => screen.getByText('Pro'))

    fireEvent.click(screen.getAllByRole('button', { name: /subscribe/i })[0])

    await waitFor(() => {
      expect(mockSubscribe).toHaveBeenCalledWith('pro-id')
      expect(window.location.href).toBe('https://checkout.stripe.com/pay/cs_test_abc')
    })
  })

  // ── FB2.3: shows current subscription status ───────────────────────────────

  it('shows_currentSubscription_status_badge', async () => {
    mockGetSubscription.mockResolvedValueOnce({
      planName: 'Pro',
      tier: 'Pro',
      status: 'Active',
      currentPeriodEnd: '2025-12-31T00:00:00Z',
    })

    render(<BillingPage onBack={vi.fn()} />)

    await waitFor(() => {
      expect(screen.getByText(/current plan/i)).toBeDefined()
      expect(screen.getByText(/Active/)).toBeDefined()
    })
  })

  // ── FB2.4: cancel subscription ─────────────────────────────────────────────

  it('cancel_button_callsCancelAndRefreshesStatus', async () => {
    mockGetSubscription
      .mockResolvedValueOnce({ planName: 'Pro', tier: 'Pro', status: 'Active', currentPeriodEnd: '2025-12-31T00:00:00Z' })
      .mockResolvedValueOnce({ planName: 'Pro', tier: 'Pro', status: 'Cancelled', currentPeriodEnd: '2025-12-31T00:00:00Z' })
    mockCancelSubscription.mockResolvedValueOnce(undefined)

    render(<BillingPage onBack={vi.fn()} />)
    await waitFor(() => screen.getByRole('button', { name: /cancel/i }))

    fireEvent.click(screen.getByRole('button', { name: /cancel/i }))

    await waitFor(() => {
      expect(mockCancelSubscription).toHaveBeenCalled()
      expect(screen.getByText(/Cancelled/)).toBeDefined()
    })
  })

  // ── FB2.5: back button ─────────────────────────────────────────────────────

  it('back_button_callsOnBack', async () => {
    const onBack = vi.fn()
    render(<BillingPage onBack={onBack} />)
    await waitFor(() => screen.getByText('Free'))

    fireEvent.click(screen.getByRole('button', { name: /back to chat/i }))

    expect(onBack).toHaveBeenCalled()
  })
})
