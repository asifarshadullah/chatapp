import { useState } from 'react'
import type { ChatMessage } from '../types/chat'
import { sendMessage } from '../services/chatApi'
import { MessageList } from './MessageList'
import { ChatInput } from './ChatInput'

export function ChatWindow() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSend(text: string) {
    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      message: text,
      role: 'user',
      timestamp: new Date().toISOString(),
    }
    setMessages((prev) => [...prev, userMessage])
    setIsLoading(true)
    setError(null)

    try {
      const response = await sendMessage(text)
      const assistantMessage: ChatMessage = {
        id: response.id,
        message: response.message,
        role: 'assistant',
        timestamp: response.timestamp,
      }
      setMessages((prev) => [...prev, assistantMessage])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="chat-window">
      <MessageList messages={messages} />
      {error && <div role="alert" className="chat-error">{error}</div>}
      <ChatInput onSend={handleSend} isLoading={isLoading} />
    </div>
  )
}
