import { useEffect, useRef, useState } from 'react'
import { Box, Button, CircularProgress, Typography, Alert } from '@mui/material'
import type { ChatMessage } from '../types/chat'
import { streamMessage } from '../services/chatApi'
import { authService } from '../services/authService'
import { SessionExpiredError, SESSION_EXPIRED_MESSAGE } from '../services/sessionErrors'
import { MessageList } from './MessageList'
import { ChatInput } from './ChatInput'

interface ChatWindowProps {
  onLogout?: () => void
  onManageBilling?: () => void
  /** Called when the session turns out to have ended. */
  onSessionExpired?: () => void
}

export function ChatWindow({ onLogout, onManageBilling, onSessionExpired }: ChatWindowProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isStreaming, setIsStreaming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [conversationId, setConversationId] = useState<string | undefined>()
  const streamingIdRef = useRef<string | null>(null)
  const streamingContentRef = useRef('')
  // Held in a ref so the connect effect stays mount-only: re-running it on a
  // new callback identity would reconnect on every parent render.
  const onSessionExpiredRef = useRef(onSessionExpired)
  useEffect(() => {
    onSessionExpiredRef.current = onSessionExpired
  }, [onSessionExpired])

  // Aborted on unmount, which is also how signing out stops a reply.
  const replyInFlightRef = useRef<AbortController | null>(null)

  // Notice an ended session on opening; other failures are left for the first send.
  useEffect(() => {
    let cancelled = false
    authService.getValidToken().catch((err: unknown) => {
      if (cancelled || !(err instanceof SessionExpiredError)) return
      setError(SESSION_EXPIRED_MESSAGE)
      onSessionExpiredRef.current?.()
    })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => () => replyInFlightRef.current?.abort(), [])

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

    const reply = new AbortController()
    replyInFlightRef.current = reply

    await streamMessage(text, conversationId, {
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
        if (err === SESSION_EXPIRED_MESSAGE) onSessionExpiredRef.current?.()
        setMessages((prev) => prev.filter((m) => m.id !== streamingIdRef.current))
        streamingIdRef.current = null
        streamingContentRef.current = ''
      },
    }, reply.signal)
  }

  const headerButtons = (onLogout || onManageBilling) && (
    <Box sx={{ position: 'absolute', top: 12, right: 16, display: 'flex', gap: 1 }}>
      {onManageBilling && (
        <Button size="small" variant="outlined" onClick={onManageBilling}>
          Manage Plan
        </Button>
      )}
      {onLogout && (
        <Button size="small" variant="outlined" onClick={onLogout}>
          Logout
        </Button>
      )}
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
        {headerButtons}
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
      {headerButtons}
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
