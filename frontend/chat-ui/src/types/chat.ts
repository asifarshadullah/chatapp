export interface ChatMessage {
  id: string
  message: string
  role: 'user' | 'assistant'
  timestamp: string
}

export interface ChatRequest {
  message: string
}

export interface ChatResponse {
  id: string
  message: string
  role: string
  timestamp: string
}
