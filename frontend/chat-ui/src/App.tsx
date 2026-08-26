import { useEffect, useState } from 'react'
import { ThemeProvider, createTheme, CssBaseline, Box, CircularProgress } from '@mui/material'
import { ChatWindow } from './components/ChatWindow'
import { LoginPage } from './components/LoginPage'
import { BillingPage } from './components/BillingPage'
import { signalRService } from './services/signalRService'
import { authService } from './services/authService'

const theme = createTheme({
  palette: {
    background: {
      default: '#ffffff',
    },
  },
  typography: {
    fontFamily: 'Roboto, sans-serif',
  },
})

function App() {
  const [isAuthenticated, setIsAuthenticated] = useState(authService.isAuthenticated())
  // A lapsed access token does not mean the session is over: the refresh cookie may still be
  // good. Deciding from the access token alone would show the sign-in form to someone who is
  // still signed in, so restoration is attempted before anything is rendered.
  const [isRestoring, setIsRestoring] = useState(
    () => !authService.isAuthenticated() && authService.hasSession(),
  )
  const [view, setView] = useState<'chat' | 'billing'>('chat')

  useEffect(() => {
    if (!isRestoring) return
    let cancelled = false
    authService
      .restoreSession()
      .then((restored) => { if (!cancelled) setIsAuthenticated(restored) })
      .finally(() => { if (!cancelled) setIsRestoring(false) })
    return () => { cancelled = true }
  }, [isRestoring])

  function endSession() {
    signalRService.stop()
    authService.logout()
    setIsAuthenticated(false)
  }

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      {isRestoring ? (
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100vh' }}>
          <CircularProgress aria-label="restoring session" />
        </Box>
      ) : isAuthenticated ? (
        view === 'billing' ? (
          <BillingPage onBack={() => setView('chat')} />
        ) : (
          <ChatWindow
            onLogout={endSession}
            onSessionExpired={endSession}
            onManageBilling={() => setView('billing')}
          />
        )
      ) : (
        <LoginPage onLogin={() => setIsAuthenticated(true)} />
      )}
    </ThemeProvider>
  )
}

export default App
