import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within, act } from '@testing-library/react'
import { BillingPage } from '../BillingPage'
import { UpgradesUnavailableError } from '../../services/billingService'
import type { SubscriptionStatusDto } from '../../types/chat'

const { mockGetPlans, mockSubscribe, mockGetSubscription, mockCancelSubscription, mockGetCheckoutMode } = vi.hoisted(() => ({
  mockGetPlans: vi.fn(),
  mockSubscribe: vi.fn(),
  mockGetSubscription: vi.fn(),
  mockCancelSubscription: vi.fn(),
  mockGetCheckoutMode: vi.fn(),
}))

vi.mock('../../services/billingService', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../services/billingService')>()),
  billingService: {
    getPlans: mockGetPlans,
    subscribe: mockSubscribe,
    getSubscription: mockGetSubscription,
    cancelSubscription: mockCancelSubscription,
    getCheckoutMode: mockGetCheckoutMode,
  },
}))

const plans = [
  { id: 'free-id', name: 'Free', tier: 'Free', pricePerMonth: 0, dailyMessageLimit: 10, features: ['Chat'] },
  { id: 'pro-id', name: 'Pro', tier: 'Pro', pricePerMonth: 19, dailyMessageLimit: 100, features: ['Chat'] },
]

const FREE: SubscriptionStatusDto = { planName: 'Free', tier: 'Free', status: null, currentPeriodEnd: null, cancelAtPeriodEnd: false }
const PRO: SubscriptionStatusDto = {
  planName: 'Pro',
  tier: 'Pro',
  status: 'Active',
  currentPeriodEnd: '2026-11-30T00:00:00Z',
  cancelAtPeriodEnd: false,
}

function card(name: string) {
  return screen.getByRole('region', { name })
}

async function opened(props: Partial<Parameters<typeof BillingPage>[0]> = {}) {
  render(<BillingPage onBack={vi.fn()} {...props} />)
  await screen.findByRole('region', { name: 'Pro' })
}

beforeEach(() => {
  vi.resetAllMocks()
  mockGetPlans.mockResolvedValue(plans)
  mockGetSubscription.mockResolvedValue(FREE)
  mockGetCheckoutMode.mockResolvedValue('test')
  Object.defineProperty(window, 'location', {
    value: { href: '' },
    writable: true,
    configurable: true,
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('BillingPage', () => {
  // ── The plans ──────────────────────────────────────────────────────────────

  it('shows Free and Pro by their daily allowance, and nothing the app does not have', async () => {
    await opened()

    expect(within(card('Free')).getByText('10 messages a day')).toBeDefined()
    expect(within(card('Free')).getByText(/\$0/)).toBeDefined()
    expect(within(card('Pro')).getByText('100 messages a day')).toBeDefined()
    expect(within(card('Pro')).getByText(/\$19/)).toBeDefined()
    expect(screen.queryByText(/document upload/i)).toBeNull()
    expect(screen.queryByText(/share/i)).toBeNull()
  })

  // ── What upgrading means here ──────────────────────────────────────────────

  it('says in test mode that this is a demo, with the test card', async () => {
    await opened()

    expect(screen.getByText(/4242 4242 4242 4242/)).toBeDefined()
    expect(screen.getByText(/nothing is charged/i)).toBeDefined()
  })

  it('says when upgrades are turned off, with Subscribe disabled', async () => {
    mockGetCheckoutMode.mockResolvedValue('off')

    await opened()

    expect(screen.getByText(/upgrades are turned off here/i)).toBeDefined()
    expect((within(card('Pro')).getByRole('button', { name: /subscribe/i }) as HTMLButtonElement).disabled).toBe(true)
  })

  // ── The current plan ───────────────────────────────────────────────────────

  it('marks Free as current for a user without a paid plan', async () => {
    await opened()

    expect(within(card('Free')).getByText('Current plan')).toBeDefined()
    expect(within(card('Pro')).queryByText('Current plan')).toBeNull()
  })

  it('marks Pro as current, with its renewal date and a way to cancel', async () => {
    mockGetSubscription.mockResolvedValue(PRO)

    await opened()

    expect(within(card('Pro')).getByText('Current plan')).toBeDefined()
    expect(within(card('Pro')).queryByRole('button', { name: /subscribe/i })).toBeNull()
    expect(screen.getByText('Renews on 30 November 2026')).toBeDefined()
    expect(screen.getByRole('button', { name: /cancel subscription/i })).toBeDefined()
  })

  it('shows when a cancelled Pro ends, with no cancel button', async () => {
    mockGetSubscription.mockResolvedValue({ ...PRO, cancelAtPeriodEnd: true })

    await opened()

    expect(screen.getByText('Ends on 30 November 2026')).toBeDefined()
    expect(screen.queryByRole('button', { name: /cancel subscription/i })).toBeNull()
  })

  it('cancelling shows when Pro ends', async () => {
    mockGetSubscription.mockResolvedValueOnce(PRO).mockResolvedValueOnce({ ...PRO, cancelAtPeriodEnd: true })
    mockCancelSubscription.mockResolvedValueOnce(undefined)

    await opened()
    fireEvent.click(screen.getByRole('button', { name: /cancel subscription/i }))

    expect(await screen.findByText('Ends on 30 November 2026')).toBeDefined()
    expect(mockCancelSubscription).toHaveBeenCalled()
  })

  // ── Starting a checkout ────────────────────────────────────────────────────

  it('sends a Free user who subscribes to Stripe', async () => {
    mockSubscribe.mockResolvedValueOnce({ checkoutUrl: 'https://checkout.stripe.com/pay/cs_test_abc', sessionId: 'cs_test_abc' })

    await opened()
    fireEvent.click(within(card('Pro')).getByRole('button', { name: /subscribe/i }))

    await waitFor(() => {
      expect(mockSubscribe).toHaveBeenCalledWith('pro-id')
      expect(window.location.href).toBe('https://checkout.stripe.com/pay/cs_test_abc')
    })
  })

  it('shows the server message when upgrades are off, and stays on the page', async () => {
    mockSubscribe.mockRejectedValueOnce(new UpgradesUnavailableError('Upgrades are off.'))

    await opened()
    fireEvent.click(within(card('Pro')).getByRole('button', { name: /subscribe/i }))

    expect((await screen.findByRole('alert')).textContent).toContain('Upgrades are off.')
    expect(window.location.href).toBe('')
    expect((within(card('Pro')).getByRole('button', { name: /subscribe/i }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('shows a general message when starting a checkout fails otherwise', async () => {
    mockSubscribe.mockRejectedValueOnce(new Error('500'))

    await opened()
    fireEvent.click(within(card('Pro')).getByRole('button', { name: /subscribe/i }))

    expect((await screen.findByRole('alert')).textContent).toContain('The upgrade could not be started. Please try again later.')
  })

  it('clears an earlier message when Subscribe is tried again', async () => {
    mockSubscribe.mockRejectedValueOnce(new Error('500')).mockReturnValueOnce(new Promise(() => {}))

    await opened()
    fireEvent.click(within(card('Pro')).getByRole('button', { name: /subscribe/i }))
    await screen.findByRole('alert')
    fireEvent.click(within(card('Pro')).getByRole('button', { name: /subscribe/i }))

    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
  })

  // ── Loading ────────────────────────────────────────────────────────────────

  it('says when Manage Plan cannot load, instead of loading forever', async () => {
    mockGetPlans.mockRejectedValue(new Error('500'))
    const onBack = vi.fn()

    render(<BillingPage onBack={onBack} />)

    expect(await screen.findByText('Manage Plan could not be loaded. Please try again later.')).toBeDefined()
    expect(screen.queryByRole('progressbar')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /back to chat/i }))
    expect(onBack).toHaveBeenCalled()
  })

  it('goes back to chat', async () => {
    const onBack = vi.fn()
    render(<BillingPage onBack={onBack} />)
    await screen.findByRole('region', { name: 'Free' })

    fireEvent.click(screen.getByRole('button', { name: /back to chat/i }))

    expect(onBack).toHaveBeenCalled()
  })

  // ── Returning from Stripe ──────────────────────────────────────────────────

  it('after paying, says the plan updates once Stripe confirms, then shows Pro', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    mockGetSubscription.mockResolvedValueOnce(FREE).mockResolvedValueOnce(FREE).mockResolvedValue(PRO)

    await opened({ returnedFrom: 'success' })
    expect(screen.getByText(/payment was received/i)).toBeDefined()

    await act(() => vi.advanceTimersByTimeAsync(6_000))

    await waitFor(() => expect(within(card('Pro')).getByText('Current plan')).toBeDefined())
    expect(screen.queryByText(/payment was received/i)).toBeNull()
    expect(mockGetSubscription).toHaveBeenCalledTimes(3)
  })

  it('after paying, gives up after ten checks and says to reload later', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    await opened({ returnedFrom: 'success' })
    await act(() => vi.advanceTimersByTimeAsync(40_000))

    expect(await screen.findByText(/has not updated yet/i)).toBeDefined()
    expect(screen.getByText(/reload the page in a minute/i)).toBeDefined()
    expect(mockGetSubscription).toHaveBeenCalledTimes(11)
  })

  it('after cancelling checkout, says nothing was charged', async () => {
    await opened({ returnedFrom: 'cancel' })

    expect(screen.getByText('Checkout was cancelled. Nothing was charged.')).toBeDefined()
  })
})
