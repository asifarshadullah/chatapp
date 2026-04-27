import type { CheckoutSessionDto, PlanDto, SubscriptionStatusDto } from '../types/chat'
import { authService } from './authService'

function authHeaders(): Record<string, string> {
  const token = authService.getToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

class BillingService {
  async getPlans(): Promise<PlanDto[]> {
    const response = await fetch('/billing/plans', {
      headers: authHeaders(),
    })
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async subscribe(planId: string): Promise<CheckoutSessionDto> {
    const response = await fetch('/billing/subscribe', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ planId }),
    })
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async getSubscription(): Promise<SubscriptionStatusDto | null> {
    const response = await fetch('/billing/subscription', {
      headers: authHeaders(),
    })
    if (response.status === 404) return null
    if (!response.ok) throw new Error(`${response.status}`)
    return response.json()
  }

  async cancelSubscription(): Promise<void> {
    const response = await fetch('/billing/subscription', {
      method: 'DELETE',
      headers: authHeaders(),
    })
    if (!response.ok) throw new Error(`${response.status}`)
  }
}

export const billingService = new BillingService()
