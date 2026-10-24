import type { ConversationHistory } from '../types/chat'
import { authorizedFetch } from './authorizedFetch'

const API_BASE = '/api'

export async function getHistory(conversationId: string): Promise<ConversationHistory> {
  const response = await authorizedFetch(`${API_BASE}/chat/${conversationId}/history`)

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`)
  }

  return response.json()
}
