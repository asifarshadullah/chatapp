import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from '../../App'
import { authService } from '../../services/authService'
import { signalRService } from '../../services/signalRService'

vi.mock('../../services/authService', () => ({
  authService: {
    isAuthenticated: vi.fn(),
    login: vi.fn(),
    register: vi.fn(),
    logout: vi.fn(),
    getToken: vi.fn().mockReturnValue(null),
  },
}))

vi.mock('../../services/signalRService', () => ({
  signalRService: {
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    sendMessage: vi.fn(),
  },
}))

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(signalRService.start).mockResolvedValue(undefined)
  vi.mocked(signalRService.stop).mockResolvedValue(undefined)
})

// ── Cycle FA3.1 ──────────────────────────────────────────────────────────────

it('shows LoginPage when unauthenticated', () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(false)

  render(<App />)

  expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  expect(screen.getByLabelText(/password/i)).toBeInTheDocument()
})

// ── Cycle FA3.2 ──────────────────────────────────────────────────────────────

it('shows ChatWindow when authenticated', () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)

  render(<App />)

  expect(screen.getByRole('textbox')).toBeInTheDocument() // chat textarea
})

// ── Cycle FA3.4 ──────────────────────────────────────────────────────────────

it('returns to LoginPage after logout', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)

  render(<App />)
  expect(screen.getByRole('textbox')).toBeInTheDocument() // chat textarea

  await userEvent.click(screen.getByRole('button', { name: /logout/i }))

  await waitFor(() => {
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  })
  expect(authService.logout).toHaveBeenCalledOnce()
})

// ── Cycle FA3.3 ──────────────────────────────────────────────────────────────

it('transitions to ChatWindow after successful login', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(false)
  vi.mocked(authService.login).mockResolvedValue({
    accessToken: 'tok',
    expiresAt: '',
    userId: 'uid',
  })

  render(<App />)
  expect(screen.getByLabelText(/email/i)).toBeInTheDocument()

  await userEvent.type(screen.getByLabelText(/email/i), 'user@test.com')
  await userEvent.type(screen.getByLabelText(/password/i), 'Password123')
  await userEvent.click(screen.getByRole('button', { name: /login/i }))

  await waitFor(() => {
    expect(screen.getByRole('textbox')).toBeInTheDocument() // chat textarea
  })
})
