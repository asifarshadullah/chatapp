import { useEffect, useRef, useState } from 'react'
import { Box, Button, CircularProgress, IconButton, Typography, Alert } from '@mui/material'
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined'
import type { ChatMessage } from '../types/chat'
import { streamMessage, getHistory, getModels, CONVERSATION_NOT_FOUND_MESSAGE } from '../services/chatApi'
import type { ModelsResult } from '../services/chatApi'
import { modelInUse, savedModel, saveModel } from '../services/modelChoice'
import {
  conversationInUrl,
  isConversationId,
  showConversationInUrl,
  removeConversationFromUrl,
} from '../services/conversationUrl'
import { authService } from '../services/authService'
import { SessionExpiredError, SESSION_EXPIRED_MESSAGE } from '../services/sessionErrors'
import { MessageList } from './MessageList'
import { ChatInput } from './ChatInput'
import { SettingsDialog } from './SettingsDialog'

interface ChatWindowProps {
  onLogout?: () => void
  onManageBilling?: () => void
  /** Called when the session turns out to have ended. */
  onSessionExpired?: () => void
}

type HistoryState = 'none' | 'loading' | 'failed'

export function ChatWindow({ onLogout, onManageBilling, onSessionExpired }: ChatWindowProps) {
  const [requested] = useState(conversationInUrl)
  const requestedIsValid = requested !== null && isConversationId(requested)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isStreaming, setIsStreaming] = useState(false)
  const [error, setError] = useState<string | null>(
    requested !== null && !requestedIsValid ? CONVERSATION_NOT_FOUND_MESSAGE : null,
  )
  const [conversationId, setConversationId] = useState<string | undefined>()
  const [historyState, setHistoryState] = useState<HistoryState>(requestedIsValid ? 'loading' : 'none')
  const [models, setModels] = useState<ModelsResult | null>(null)
  // Until the list arrives, the saved choice is used unchecked; the server refuses a stale one.
  const [model, setModel] = useState<string | null>(savedModel)
  const [settingsOpen, setSettingsOpen] = useState(false)
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

  useEffect(() => {
    let cancelled = false
    getModels().then((result) => {
      if (cancelled) return
      setModels(result)
      setModel('error' in result ? null : modelInUse(result))
    })
    return () => {
      cancelled = true
    }
  }, [])

  function applyModel(id: string) {
    setModel(id)
    saveModel(id)
    setSettingsOpen(false)
  }

  useEffect(() => {
    if (requested === null) return
    if (!isConversationId(requested)) {
      removeConversationFromUrl()
      return
    }
    let cancelled = false
    getHistory(requested).then((result) => {
      if (cancelled) return
      if ('messages' in result) {
        setMessages(result.messages)
        setConversationId(requested)
        setHistoryState('none')
        return
      }
      setError(result.error)
      if (result.error === CONVERSATION_NOT_FOUND_MESSAGE) {
        removeConversationFromUrl()
        setHistoryState('none')
        return
      }
      setHistoryState('failed')
      if (result.error === SESSION_EXPIRED_MESSAGE) onSessionExpiredRef.current?.()
    })
    return () => {
      cancelled = true
    }
  }, [requested])

  function startNewChat() {
    setMessages([])
    setError(null)
    setConversationId(undefined)
    setHistoryState('none')
    removeConversationFromUrl()
  }

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
      onConversationId: (id) => {
        setConversationId(id)
        showConversationInUrl(id)
      },
      onWord: (word) => {
        streamingContentRef.current += word
        const content = streamingContentRef.current
        // Read now, not when React applies the update: by then onComplete may have cleared it,
        // and the update would add a second bubble.
        const replyId = streamingId
        setMessages((prev) => {
          const exists = prev.some((m) => m.id === replyId)
          if (exists) {
            return prev.map((m) =>
              m.id === replyId ? { ...m, message: content } : m,
            )
          }
          return [
            ...prev,
            {
              id: replyId,
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
        // What arrived stays on screen: the server saves it too, so a reload shows the same.
        streamingIdRef.current = null
        streamingContentRef.current = ''
      },
    }, reply.signal, model ?? undefined)
  }

  const inputBlocked = isStreaming || historyState !== 'none'
  const canStartNewChat = messages.length > 0 || error !== null

  const headerButtons = (
    <Box sx={{ position: 'absolute', top: 12, right: 16, display: 'flex', gap: 1, alignItems: 'center' }}>
      <IconButton size="small" aria-label="Settings" onClick={() => setSettingsOpen(true)}>
        <SettingsOutlinedIcon fontSize="small" />
      </IconButton>
      {canStartNewChat && (
        <Button size="small" variant="outlined" onClick={startNewChat} disabled={isStreaming}>
          New chat
        </Button>
      )}
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
      <SettingsDialog
        open={settingsOpen}
        models={models}
        model={model}
        onSave={applyModel}
        onCancel={() => setSettingsOpen(false)}
      />
    </Box>
  )

  if (messages.length === 0 && !error && historyState === 'none') {
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
        <ChatInput onSend={handleSend} isLoading={inputBlocked} />
      </Box>
    )
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh', position: 'relative' }}>
      {headerButtons}
      {historyState === 'loading' ? (
        <Box sx={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <CircularProgress aria-label="loading conversation" />
        </Box>
      ) : (
        <MessageList messages={messages} />
      )}
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
        <ChatInput onSend={handleSend} isLoading={inputBlocked} />
      </Box>
    </Box>
  )
}
