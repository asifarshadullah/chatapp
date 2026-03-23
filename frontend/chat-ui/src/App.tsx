import { useState } from 'react'
import { ThemeProvider, createTheme, CssBaseline } from '@mui/material'
import { ChatWindow } from './components/ChatWindow'
import { LoginPage } from './components/LoginPage'
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

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      {isAuthenticated ? (
        <ChatWindow onLogout={() => { authService.logout(); setIsAuthenticated(false) }} />
      ) : (
        <LoginPage onLogin={() => setIsAuthenticated(true)} />
      )}
    </ThemeProvider>
  )
}

export default App
