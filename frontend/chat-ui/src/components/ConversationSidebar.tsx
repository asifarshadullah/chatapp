import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  List,
  ListItemButton,
  ListItemText,
  Typography,
} from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import { listConversations } from '../services/chatApi'
import type { ConversationSummary } from '../services/chatApi'
import { SESSION_EXPIRED_MESSAGE } from '../services/sessionErrors'

const MORE_FAILED_MESSAGE = 'More conversations could not be loaded.'

interface ConversationSidebarProps {
  /** The conversation the chat shows, marked as the current entry. */
  currentId?: string
  /** A reply is streaming, so nothing here may be chosen. */
  busy: boolean
  canStartNewChat: boolean
  /** Changing it reloads the first page, after a message was sent. */
  refreshKey: number
  onOpen: (conversationId: string) => void
  onNewChat: () => void
  onSessionExpired: () => void
}

type ListState =
  | { status: 'loading' }
  | { status: 'failed'; error: string }
  | { status: 'loaded'; entries: ConversationSummary[]; nextCursor: string | null }

/** Later entries keep their place unless they are among the first ones, so none shows twice. */
function withoutRepeats(first: ConversationSummary[], rest: ConversationSummary[]) {
  const shown = new Set(first.map((c) => c.id))
  return [...first, ...rest.filter((c) => !shown.has(c.id))]
}

export function ConversationSidebar({
  currentId,
  busy,
  canStartNewChat,
  refreshKey,
  onOpen,
  onNewChat,
  onSessionExpired,
}: ConversationSidebarProps) {
  const [list, setList] = useState<ListState>({ status: 'loading' })
  const [more, setMore] = useState<'idle' | 'loading' | 'failed'>('idle')
  const onSessionExpiredRef = useRef(onSessionExpired)
  useEffect(() => {
    onSessionExpiredRef.current = onSessionExpired
  }, [onSessionExpired])

  /** Loads the first page; a reload keeps what is shown when it fails. */
  const loadFirstPage = useCallback((isCancelled: () => boolean) => {
    listConversations().then((result) => {
      if (isCancelled()) return
      if ('error' in result) {
        if (result.error === SESSION_EXPIRED_MESSAGE) onSessionExpiredRef.current()
        setList((current) => (current.status === 'loaded' ? current : { status: 'failed', error: result.error }))
        return
      }
      setList((current) =>
        current.status === 'loaded'
          ? // Pages already shown stay, and so does the cursor that continues after them.
            { ...current, entries: withoutRepeats(result.conversations, current.entries) }
          : { status: 'loaded', entries: result.conversations, nextCursor: result.nextCursor },
      )
    })
  }, [])

  useEffect(() => {
    let cancelled = false
    loadFirstPage(() => cancelled)
    return () => {
      cancelled = true
    }
  }, [refreshKey, loadFirstPage])

  function retry() {
    setList({ status: 'loading' })
    loadFirstPage(() => false)
  }

  function showMore() {
    if (list.status !== 'loaded' || list.nextCursor === null) return
    setMore('loading')
    listConversations(list.nextCursor).then((result) => {
      if ('error' in result) {
        if (result.error === SESSION_EXPIRED_MESSAGE) onSessionExpiredRef.current()
        setMore('failed')
        return
      }
      setMore('idle')
      setList((current) =>
        current.status === 'loaded'
          ? {
              status: 'loaded',
              entries: withoutRepeats(current.entries, result.conversations),
              nextCursor: result.nextCursor,
            }
          : current,
      )
    })
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      <Box sx={{ p: 1.5 }}>
        <Button
          fullWidth
          variant="outlined"
          startIcon={<AddIcon />}
          onClick={onNewChat}
          disabled={busy || !canStartNewChat}
        >
          New chat
        </Button>
      </Box>
      <Box sx={{ flex: 1, overflowY: 'auto', px: 1, pb: 1.5 }}>
        {list.status === 'loading' && (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 3 }}>
            <CircularProgress size={24} aria-label="loading conversations" />
          </Box>
        )}
        {list.status === 'failed' && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={retry}>
                Retry
              </Button>
            }
          >
            {list.error}
          </Alert>
        )}
        {list.status === 'loaded' && list.entries.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ px: 1, py: 2 }}>
            No conversations yet.
          </Typography>
        )}
        {list.status === 'loaded' && list.entries.length > 0 && (
          <>
            <List dense aria-label="Conversations" disablePadding>
              {list.entries.map((conversation) => {
                const isCurrent = conversation.id === currentId
                return (
                  <ListItemButton
                    key={conversation.id}
                    selected={isCurrent}
                    aria-current={isCurrent ? 'page' : undefined}
                    disabled={busy}
                    onClick={() => {
                      if (!isCurrent) onOpen(conversation.id)
                    }}
                    sx={{ borderRadius: 1 }}
                  >
                    <ListItemText
                      primary={conversation.title}
                      slotProps={{ primary: { noWrap: true, title: conversation.title } }}
                    />
                  </ListItemButton>
                )
              })}
            </List>
            {more === 'failed' && (
              <Typography variant="body2" color="error" sx={{ px: 1, pt: 1 }}>
                {MORE_FAILED_MESSAGE}
              </Typography>
            )}
            {list.nextCursor !== null && (
              <Button fullWidth size="small" onClick={showMore} disabled={more === 'loading'} sx={{ mt: 1 }}>
                Show more
              </Button>
            )}
          </>
        )}
      </Box>
    </Box>
  )
}
