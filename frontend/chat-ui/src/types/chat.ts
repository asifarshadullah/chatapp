export interface ChatMessage {
  id: string
  message: string
  role: 'user' | 'assistant'
  timestamp: string
}

export interface ChatRequest {
  message: string
  conversationId?: string
}

export interface ChatResponse {
  id: string
  message: string
  role: string
  timestamp: string
  conversationId: string
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
  tier: 'Free' | 'Pro' | 'Enterprise'
  pricePerMonth: number
  features: string[]
}

export interface SubscriptionStatusDto {
  planName: string
  tier: string
  status: 'Active' | 'Cancelled' | 'PastDue' | 'Trialing'
  currentPeriodEnd: string
}

export interface CheckoutSessionDto {
  checkoutUrl: string
  sessionId: string
}
