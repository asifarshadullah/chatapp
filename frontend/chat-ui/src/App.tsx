import { useState } from 'react'
import { ThemeProvider, createTheme, CssBaseline } from '@mui/material'
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
  const [view, setView] = useState<'chat' | 'billing'>('chat')

  function endSession() {
    signalRService.stop()
    authService.logout()
    setIsAuthenticated(false)
  }

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      {isAuthenticated ? (
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
