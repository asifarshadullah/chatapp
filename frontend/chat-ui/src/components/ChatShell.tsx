import { useCallback, useState } from 'react'
import { Box, Drawer, IconButton, useMediaQuery, useTheme } from '@mui/material'
import MenuIcon from '@mui/icons-material/Menu'
import { ChatWindow } from './ChatWindow'
import { ConversationSidebar } from './ConversationSidebar'
import { showConversationInUrl, removeConversationFromUrl } from '../services/conversationUrl'

const SIDEBAR_WIDTH = 280

interface ChatShellProps {
  onLogout?: () => void
  onManageBilling?: () => void
  /** Called when the session turns out to have ended. */
  onSessionExpired?: () => void
}

/**
 * The chat beside the list of saved conversations. Opening a conversation or starting a new one
 * sets the URL and remounts the chat, which then loads whatever the URL names, exactly as on a
 * page load, so no state from the previous conversation can carry over.
 */
export function ChatShell({ onLogout, onManageBilling, onSessionExpired }: ChatShellProps) {
  const theme = useTheme()
  const isWide = useMediaQuery(theme.breakpoints.up('md'), { noSsr: true })
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [chatKey, setChatKey] = useState(0)
  const [currentId, setCurrentId] = useState<string | undefined>()
  const [isStreaming, setIsStreaming] = useState(false)
  const [canStartNewChat, setCanStartNewChat] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)

  const handleSendFinished = useCallback(() => setRefreshKey((key) => key + 1), [])
  const handleSessionExpired = useCallback(() => onSessionExpired?.(), [onSessionExpired])

  function openConversation(conversationId: string) {
    showConversationInUrl(conversationId)
    setChatKey((key) => key + 1)
    setDrawerOpen(false)
  }

  function startNewChat() {
    removeConversationFromUrl()
    setChatKey((key) => key + 1)
    setDrawerOpen(false)
  }

  const sidebar = (
    <Box component="nav" aria-label="Saved conversations" sx={{ width: SIDEBAR_WIDTH, height: '100%' }}>
      <ConversationSidebar
        currentId={currentId}
        busy={isStreaming}
        canStartNewChat={canStartNewChat}
        refreshKey={refreshKey}
        onOpen={openConversation}
        onNewChat={startNewChat}
        onSessionExpired={handleSessionExpired}
      />
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', height: '100vh' }}>
      {isWide ? (
        <Drawer
          variant="permanent"
          sx={{ width: SIDEBAR_WIDTH, flexShrink: 0, '& .MuiDrawer-paper': { width: SIDEBAR_WIDTH } }}
        >
          {sidebar}
        </Drawer>
      ) : (
        <Drawer variant="temporary" open={drawerOpen} onClose={() => setDrawerOpen(false)}>
          {sidebar}
        </Drawer>
      )}
      <Box sx={{ flex: 1, minWidth: 0, position: 'relative' }}>
        {!isWide && (
          <IconButton
            aria-label="Open conversations"
            onClick={() => setDrawerOpen(true)}
            size="small"
            sx={{ position: 'absolute', top: 12, left: 16, zIndex: 1 }}
          >
            <MenuIcon fontSize="small" />
          </IconButton>
        )}
        <ChatWindow
          key={chatKey}
          onLogout={onLogout}
          onManageBilling={onManageBilling}
          onSessionExpired={onSessionExpired}
          onConversationChange={setCurrentId}
          onStreamingChange={setIsStreaming}
          onCanStartNewChatChange={setCanStartNewChat}
          onSendFinished={handleSendFinished}
        />
      </Box>
    </Box>
  )
}
