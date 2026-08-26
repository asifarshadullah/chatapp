import { useState } from 'react'
import {
  Box,
  Button,
  Checkbox,
  FormControlLabel,
  TextField,
  Typography,
  Alert,
  Paper,
  Stack,
} from '@mui/material'
import { authService } from '../services/authService'

interface LoginPageProps {
  onLogin: () => void
}

export function LoginPage({ onLogin }: LoginPageProps) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  // Off unless the user says otherwise: a longer session is their call to make, and the
  // safe answer on a machine that might be shared is the short one.
  const [staySignedIn, setStaySignedIn] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setIsLoading(true)
    try {
      if (mode === 'login') {
        await authService.login(email, password, staySignedIn)
      } else {
        await authService.register(email, password, displayName, staySignedIn)
      }
      onLogin()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100vh',
        bgcolor: 'background.default',
      }}
    >
      <Paper elevation={3} sx={{ p: 4, width: '100%', maxWidth: 400 }}>
        <Typography variant="h5" sx={{ mb: 3, fontWeight: 500, textAlign: 'center' }}>
          {mode === 'login' ? 'Sign in' : 'Create account'}
        </Typography>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Box component="form" onSubmit={handleSubmit}>
          <Stack spacing={2}>
            <TextField
              label="Email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              fullWidth
              required
              inputProps={{ 'aria-label': 'Email' }}
            />
            <TextField
              label="Password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              fullWidth
              required
              inputProps={{ 'aria-label': 'Password' }}
            />
            {mode === 'register' && (
              <TextField
                label="Display Name"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                fullWidth
                required
                inputProps={{ 'aria-label': 'Display Name' }}
              />
            )}
            <FormControlLabel
              control={
                <Checkbox
                  checked={staySignedIn}
                  onChange={(e) => setStaySignedIn(e.target.checked)}
                  inputProps={{ 'aria-label': 'Keep me signed in' }}
                />
              }
              label="Keep me signed in"
            />
            <Button
              type="submit"
              variant="contained"
              fullWidth
              disabled={isLoading}
            >
              {mode === 'login' ? 'Login' : 'Register'}
            </Button>
            <Button
              type="button"
              variant="text"
              fullWidth
              onClick={() => {
                setMode(mode === 'login' ? 'register' : 'login')
                setError(null)
              }}
            >
              {mode === 'login' ? 'Register' : 'Login'}
            </Button>
          </Stack>
        </Box>
      </Paper>
    </Box>
  )
}
