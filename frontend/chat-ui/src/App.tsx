import { useEffect, useState } from 'react'
import { ThemeProvider, createTheme, CssBaseline, Box, CircularProgress } from '@mui/material'
import { ChatShell } from './components/ChatShell'
import { LoginPage } from './components/LoginPage'
import { BillingPage } from './components/BillingPage'
import { authService } from './services/authService'
import { removeConversationFromUrl } from './services/conversationUrl'

/** Stripe sends the user back to one of these after a checkout. */
function returnFromCheckout(): 'success' | 'cancel' | undefined {
  const path = window.location.pathname
  if (path === '/billing/success') return 'success'
  if (path === '/billing/cancel') return 'cancel'
  return undefined
}

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
  // Read before the user is known, so it survives restoring the session or signing in first.
  const [returnedFrom, setReturnedFrom] = useState(returnFromCheckout)
  const [view, setView] = useState<'chat' | 'billing'>(returnedFrom ? 'billing' : 'chat')

  useEffect(() => {
    if (returnedFrom) window.history.replaceState(null, '', '/')
  }, [returnedFrom])

  useEffect(() => {
    if (!isRestoring) return
    let cancelled = false
    authService
      .restoreSession()
      .then((restored) => { if (!cancelled) setIsAuthenticated(restored) })
      .finally(() => { if (!cancelled) setIsRestoring(false) })
    return () => { cancelled = true }
  }, [isRestoring])

  /**
   * The user asked to sign out, so the credential is revoked as well as discarded. Revocation
   * takes down the whole family, ending the session everywhere it is open — which is exactly
   * what signing out means.
   */
  function endSession() {
    authService.logout()
    removeConversationFromUrl()
    setIsAuthenticated(false)
  }

  /**
   * The session ended by itself. Nobody asked for the credential to be revoked, and revoking
   * it here would be destructive: a client can reach this point without ever contacting the
   * server — an access token can lapse while the refresh credential is still perfectly
   * exchangeable — so signing out would throw away a session that could have continued, and
   * take every other tab of it down too.
   *
   * Kept separate from endSession rather than folded into it behind a flag: the two are only
   * one call apart, and it was a single function serving both intentions that made the
   * destructive one the default.
   */
  function abandonSession() {
    authService.clearLocal()
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
          <BillingPage
            returnedFrom={returnedFrom}
            onBack={() => {
              setReturnedFrom(undefined)
              setView('chat')
            }}
          />
        ) : (
          <ChatShell
            onLogout={endSession}
            onSessionExpired={abandonSession}
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
