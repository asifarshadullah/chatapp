import type { ChatResponse, ConversationHistory } from '../types/chat'
import { authService } from './authService'

const API_BASE = '/api'

function authHeaders(): Record<string, string> {
  const token = authService.getToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

export async function sendMessage(message: string, conversationId?: string): Promise<ChatResponse> {
  const response = await fetch(`${API_BASE}/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ message, conversationId }),
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`)
  }

  return response.json()
}

export async function getHistory(conversationId: string): Promise<ConversationHistory> {
  const response = await fetch(`${API_BASE}/chat/${conversationId}/history`, {
    headers: authHeaders(),
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`)
  }

  return response.json()
}
