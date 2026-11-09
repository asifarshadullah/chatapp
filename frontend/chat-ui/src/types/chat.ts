export interface ChatMessage {
  id: string
  message: string
  role: 'user' | 'assistant'
  timestamp: string
}

export interface ChatMessageRecord {
  id: string
  content: string
  role: string
  timestamp: string
}

export interface ConversationHistory {
  conversationId: string
  messages: ChatMessageRecord[]
}

export interface PlanDto {
  id: string
  name: string
  tier: 'Free' | 'Pro'
  pricePerMonth: number
  /** Messages a user on this plan may send per UTC day. */
  dailyMessageLimit: number
  features: string[]
}

/** The user's current plan. On Free, status and currentPeriodEnd are null. */
export interface SubscriptionStatusDto {
  planName: string
  tier: string
  status: 'Active' | 'Cancelled' | 'PastDue' | 'Trialing' | null
  currentPeriodEnd: string | null
  cancelAtPeriodEnd: boolean
}

/** Whether users can upgrade here, and whether Stripe takes real money when they do. */
export type CheckoutMode = 'off' | 'test' | 'live'

export interface CheckoutSessionDto {
  checkoutUrl: string
  sessionId: string
}
