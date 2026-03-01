import { useState } from 'react'
import { Box, Typography, Alert } from '@mui/material'
import type { ChatMessage } from '../types/chat'
import { sendMessage } from '../services/chatApi'
import { MessageList } from './MessageList'
import { ChatInput } from './ChatInput'

export function ChatWindow() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [conversationId, setConversationId] = useState<string | undefined>(undefined)

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
      const response = await sendMessage(text, conversationId)
      if (!conversationId) {
        setConversationId(response.conversationId)
      }
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

  if (messages.length === 0 && !error) {
    return (
      <Box
        sx={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100vh',
          gap: 4,
          px: 2,
        }}
      >
        <Typography
          variant="h4"
          sx={{ fontWeight: 400, color: 'text.primary', textAlign: 'center' }}
        >
          What's on the agenda today?
        </Typography>
        <ChatInput onSend={handleSend} isLoading={isLoading} />
      </Box>
    )
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh' }}>
      <MessageList messages={messages} />
      {error && (
        <Alert role="alert" severity="error" sx={{ borderRadius: 0 }}>
          {error}
        </Alert>
      )}
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'center',
          px: 2,
          py: 1.5,
          borderTop: '1px solid #e0e0e0',
        }}
      >
        <ChatInput onSend={handleSend} isLoading={isLoading} />
      </Box>
    </Box>
  )
}
