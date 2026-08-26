import type { ChatResponse, ConversationHistory } from '../types/chat'
import { authorizedFetch } from './authorizedFetch'

const API_BASE = '/api'

export async function sendMessage(message: string, conversationId?: string): Promise<ChatResponse> {
  const response = await authorizedFetch(`${API_BASE}/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message, conversationId }),
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`)
  }

  return response.json()
}

export async function getHistory(conversationId: string): Promise<ConversationHistory> {
  const response = await authorizedFetch(`${API_BASE}/chat/${conversationId}/history`)

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`)
  }

  return response.json()
}
