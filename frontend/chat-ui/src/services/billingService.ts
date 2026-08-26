import type { CheckoutSessionDto, PlanDto, SubscriptionStatusDto } from '../types/chat'
import { authorizedFetch } from './authorizedFetch'

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
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async getSubscription(): Promise<SubscriptionStatusDto | null> {
    const response = await authorizedFetch('/billing/subscription')
    if (response.status === 404) return null
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async cancelSubscription(): Promise<void> {
    const response = await authorizedFetch('/billing/subscription', {
      method: 'DELETE',
    })
    if (!response.ok) throw new Error(`${response.status}`)
  }
}

export const billingService = new BillingService()
