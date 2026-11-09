import type { CheckoutMode, CheckoutSessionDto, PlanDto, SubscriptionStatusDto } from '../types/chat'
import { authorizedFetch } from './authorizedFetch'

/** The server has no payment provider set up. Its message is meant for the user. */
export class UpgradesUnavailableError extends Error {}

class BillingService {
  async getPlans(): Promise<PlanDto[]> {
    const response = await authorizedFetch('/billing/plans')
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async subscribe(planId: string): Promise<CheckoutSessionDto> {
    const response = await authorizedFetch('/billing/subscribe', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ planId }),
    })
    if (response.status === 503) throw new UpgradesUnavailableError(await response.text())
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async getSubscription(): Promise<SubscriptionStatusDto> {
    const response = await authorizedFetch('/billing/subscription')
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async getCheckoutMode(): Promise<CheckoutMode> {
    const response = await authorizedFetch('/billing/checkout-mode')
    if (!response.ok) throw new Error(`${response.status}`)
    const { mode } = (await response.json()) as { mode: CheckoutMode }
    return mode
  }

  async cancelSubscription(): Promise<void> {
    const response = await authorizedFetch('/billing/subscription', {
      method: 'DELETE',
    })
    if (!response.ok) throw new Error(`${response.status}`)
  }
}

export const billingService = new BillingService()
