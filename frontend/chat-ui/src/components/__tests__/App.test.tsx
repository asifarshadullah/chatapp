import { it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from '../../App'
import { authService } from '../../services/authService'
import { signalRService, SessionExpiredError } from '../../services/signalRService'

vi.mock('../../services/authService', () => ({
  authService: {
    isAuthenticated: vi.fn(),
    login: vi.fn(),
    register: vi.fn(),
    logout: vi.fn(),
    getToken: vi.fn().mockReturnValue(null),
    getValidToken: vi.fn(),
    hasSession: vi.fn().mockReturnValue(false),
    restoreSession: vi.fn().mockResolvedValue(false),
    refresh: vi.fn(),
    clearLocal: vi.fn(),
  },
}))

vi.mock('../../services/signalRService', async (importOriginal) => ({
  // Keep the real error class and message constants: the component compares
  // against them, so stubbing them out would make these tests pass on
  // mismatched strings.
  ...(await importOriginal<typeof import('../../services/signalRService')>()),
  signalRService: {
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    sendMessage: vi.fn(),
  },
}))

vi.mock('../../services/billingService', () => ({
  billingService: {
    getPlans: vi.fn().mockResolvedValue([]),
    getSubscription: vi.fn().mockResolvedValue(null),
    subscribe: vi.fn(),
    cancelSubscription: vi.fn(),
  },
}))

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(signalRService.start).mockResolvedValue(undefined)
  vi.mocked(signalRService.stop).mockResolvedValue(undefined)
  vi.mocked(authService.hasSession).mockReturnValue(false)
  vi.mocked(authService.restoreSession).mockResolvedValue(false)
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

// ── Cycle FB3.2 ──────────────────────────────────────────────────────────────

it('App_whenAuthenticated_canNavigateToBillingView', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)

  render(<App />)
  expect(screen.getByRole('textbox')).toBeInTheDocument() // chat textarea

  await userEvent.click(screen.getByRole('button', { name: /manage plan/i }))

  await waitFor(() => {
    expect(screen.getByText(/manage plan/i)).toBeInTheDocument() // BillingPage heading
  })

  await userEvent.click(screen.getByRole('button', { name: /back to chat/i }))

  await waitFor(() => {
    expect(screen.getByRole('textbox')).toBeInTheDocument()
  })
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

// ── Expired session recovery ─────────────────────────────────────────────────

it('returns to the sign in form when the session has expired', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)
  vi.mocked(signalRService.start).mockRejectedValue(new SessionExpiredError())

  render(<App />)

  // An expired session must land the user on login, not strand them on a chat
  // page showing a connection error they cannot act on.
  expect(await screen.findByLabelText(/password/i)).toBeInTheDocument()
  expect(authService.logout).toHaveBeenCalled()
  expect(signalRService.stop).toHaveBeenCalled()
})

// ── Restoring a lapsed session on load ───────────────────────────────────────

it('restores a session whose access token lapsed instead of showing sign in', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(false)
  vi.mocked(authService.hasSession).mockReturnValue(true)
  vi.mocked(authService.restoreSession).mockResolvedValue(true)

  render(<App />)

  // A lapsed access token with a live refresh cookie is still a signed-in user.
  expect(await screen.findByRole('textbox')).toBeInTheDocument()
  expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument()
})

it('shows sign in when the lapsed session cannot be restored', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(false)
  vi.mocked(authService.hasSession).mockReturnValue(true)
  vi.mocked(authService.restoreSession).mockResolvedValue(false)

  render(<App />)

  expect(await screen.findByLabelText(/password/i)).toBeInTheDocument()
})

it('does not attempt restoration when no session was established', () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(false)
  vi.mocked(authService.hasSession).mockReturnValue(false)

  render(<App />)

  expect(authService.restoreSession).not.toHaveBeenCalled()
  expect(screen.getByLabelText(/password/i)).toBeInTheDocument()
})
