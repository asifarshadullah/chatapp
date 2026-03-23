import { useEffect, useRef, useState } from 'react'
import { Box, Button, CircularProgress, Typography, Alert } from '@mui/material'
import type { ChatMessage } from '../types/chat'
import { signalRService } from '../services/signalRService'
import { MessageList } from './MessageList'
import { ChatInput } from './ChatInput'

interface ChatWindowProps {
  onLogout?: () => void
}

export function ChatWindow({ onLogout }: ChatWindowProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isStreaming, setIsStreaming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [conversationId, setConversationId] = useState<string | undefined>()
  const streamingIdRef = useRef<string | null>(null)
  const streamingContentRef = useRef('')

  useEffect(() => {
    let cancelled = false
    signalRService
      .start()
      .then(() => { if (!cancelled) setError(null) })
      .catch(() => { if (!cancelled) setError('Failed to connect to chat server.') })
    return () => {
      cancelled = true
      signalRService.stop()
    }
  }, [])

  async function handleSend(text: string) {
    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      message: text,
      role: 'user',
      timestamp: new Date().toISOString(),
    }
    setMessages((prev) => [...prev, userMessage])
    setIsStreaming(true)
    setError(null)

    const streamingId = crypto.randomUUID()
    streamingIdRef.current = streamingId
    streamingContentRef.current = ''

    await signalRService.sendMessage(text, conversationId, {
      onConversationId: (id) => setConversationId(id),
      onWord: (word) => {
        streamingContentRef.current += word
        const content = streamingContentRef.current
        setMessages((prev) => {
          const exists = prev.some((m) => m.id === streamingIdRef.current)
          if (exists) {
            return prev.map((m) =>
              m.id === streamingIdRef.current ? { ...m, message: content } : m,
            )
          }
          return [
            ...prev,
            {
              id: streamingIdRef.current!,
              message: content,
              role: 'assistant' as const,
              timestamp: new Date().toISOString(),
            },
          ]
        })
      },
      onComplete: () => {
        setIsStreaming(false)
        streamingIdRef.current = null
        streamingContentRef.current = ''
      },
      onError: (err) => {
        setIsStreaming(false)
        setError(err)
        setMessages((prev) => prev.filter((m) => m.id !== streamingIdRef.current))
        streamingIdRef.current = null
        streamingContentRef.current = ''
      },
    })
  }

  const logoutButton = onLogout && (
    <Box sx={{ position: 'absolute', top: 12, right: 16 }}>
      <Button size="small" variant="outlined" onClick={onLogout}>
        Logout
      </Button>
    </Box>
  )

  if (messages.length === 0 && !error) {
    return (
      <Box
        sx={{
          position: 'relative',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100vh',
          gap: 4,
          px: 2,
        }}
      >
        {logoutButton}
        <Typography
          variant="h4"
          sx={{ fontWeight: 400, color: 'text.primary', textAlign: 'center' }}
        >
          What's on the agenda today?
        </Typography>
        <ChatInput onSend={handleSend} isLoading={isStreaming} />
      </Box>
    )
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh', position: 'relative' }}>
      {logoutButton}
      <MessageList messages={messages} />
      {isStreaming && (
        <Box
          aria-label="typing indicator"
          sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 3, py: 1 }}
        >
          <CircularProgress size={14} thickness={5} />
          <Typography variant="caption" color="text.secondary">
            Assistant is typing…
          </Typography>
        </Box>
      )}
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
        <ChatInput onSend={handleSend} isLoading={isStreaming} />
      </Box>
    </Box>
  )
}
