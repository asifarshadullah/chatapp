import { it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from '../../App'
import { authService } from '../../services/authService'
import { SessionExpiredError } from '../../services/sessionErrors'
import { listConversations } from '../../services/chatApi'

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

vi.mock('../../services/chatApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../services/chatApi')>()),
  getHistory: vi.fn().mockResolvedValue({ messages: [] }),
  getModels: vi.fn().mockResolvedValue({ defaultModel: 'local', models: [{ id: 'local', name: 'Local · gemma3:4b' }] }),
  listConversations: vi.fn().mockResolvedValue({ conversations: [], nextCursor: null }),
}))

vi.mock('../../services/billingService', () => ({
  billingService: {
    getPlans: vi.fn().mockResolvedValue([]),
    getSubscription: vi.fn().mockResolvedValue(null),
    subscribe: vi.fn(),
    cancelSubscription: vi.fn(),
  },
}))

const SAVED_ID = '3f2c1d4e-5a6b-4c7d-8e9f-0a1b2c3d4e5f'

function conversationInUrl() {
  return new URLSearchParams(window.location.search).get('conversation')
}

beforeEach(() => {
  vi.clearAllMocks()
  window.history.replaceState(null, '', '/')
  vi.mocked(authService.getValidToken).mockResolvedValue('token')
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

// ── The sidebar shows only the signed-in user's conversations ─────────────────

afterEach(() => vi.unstubAllGlobals())

it("never shows the previous user's conversations to the next one in the same tab", async () => {
  // A wide window, so the sidebar is on screen without opening it.
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('min-width'),
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }))
  const alices = { id: 'a1', title: "Alice's plans", lastActivityAt: '2026-10-26T10:00:00Z' }
  const bobs = { id: 'b1', title: "Bob's notes", lastActivityAt: '2026-10-26T11:00:00Z' }
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)
  vi.mocked(listConversations).mockResolvedValue({ conversations: [alices], nextCursor: null })
  vi.mocked(authService.login).mockResolvedValue({ accessToken: 'tok', expiresAt: '', userId: 'bob' })

  render(<App />)
  expect(await screen.findByText("Alice's plans")).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: /logout/i }))
  await waitFor(() => expect(screen.getByLabelText(/email/i)).toBeInTheDocument())
  expect(screen.queryByText("Alice's plans")).not.toBeInTheDocument()

  // Bob's list is held back, so the moment before it arrives is seen too.
  let answer!: (page: { conversations: typeof bobs[]; nextCursor: null }) => void
  vi.mocked(listConversations).mockReturnValue(new Promise((resolve) => (answer = resolve)))
  await userEvent.type(screen.getByLabelText(/email/i), 'bob@test.com')
  await userEvent.type(screen.getByLabelText(/password/i), 'Password123')
  await userEvent.click(screen.getByRole('button', { name: /login/i }))

  await waitFor(() => expect(screen.getByRole('textbox')).toBeInTheDocument())
  expect(screen.queryByText("Alice's plans")).not.toBeInTheDocument()
  answer({ conversations: [bobs], nextCursor: null })
  expect(await screen.findByText("Bob's notes")).toBeInTheDocument()
  expect(screen.queryByText("Alice's plans")).not.toBeInTheDocument()
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
  vi.mocked(authService.getValidToken).mockRejectedValue(new SessionExpiredError())

  render(<App />)

  // An expired session must land the user on login, not strand them on a chat
  // page showing a connection error they cannot act on.
  expect(await screen.findByLabelText(/password/i)).toBeInTheDocument()
  expect(authService.clearLocal).toHaveBeenCalled()
})

it('does not sign out on the server when the session ended by itself', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)
  vi.mocked(authService.getValidToken).mockRejectedValue(new SessionExpiredError())

  render(<App />)
  await screen.findByLabelText(/password/i)

  // Signing out revokes the credential family, which ends the session for every
  // client holding it and cannot be undone. The user did not ask for that, and
  // the credential may still be perfectly exchangeable — the client can reach
  // this point without having contacted the server at all.
  expect(authService.logout).not.toHaveBeenCalled()
  expect(authService.clearLocal).toHaveBeenCalled()
})

it('still signs out on the server when the user chooses to', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)

  render(<App />)
  await userEvent.click(await screen.findByRole('button', { name: /logout/i }))

  // A deliberate sign-out is the one case where revoking is the point: the
  // credential may still be good, and leaving it live is the risk.
  expect(authService.logout).toHaveBeenCalledOnce()
  expect(await screen.findByLabelText(/password/i)).toBeInTheDocument()
})

it('leaves no access token behind when a session is abandoned', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)
  vi.mocked(authService.getValidToken).mockRejectedValue(new SessionExpiredError())

  render(<App />)
  await screen.findByLabelText(/password/i)

  // Not signing out on the server must not mean holding on locally: the
  // user-visible outcome is unchanged, which is what makes this safe to ship.
  expect(authService.clearLocal).toHaveBeenCalled()
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

// ── The conversation in the URL ──────────────────────────────────────────────

it('removes the conversation from the URL when the user signs out', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)
  window.history.replaceState(null, '', `/?conversation=${SAVED_ID}`)
  render(<App />)

  await userEvent.click(screen.getByRole('button', { name: /logout/i }))

  await waitFor(() => expect(screen.getByLabelText(/email/i)).toBeInTheDocument())
  expect(conversationInUrl()).toBeNull()
})

it('keeps the conversation in the URL when the session ends by itself', async () => {
  vi.mocked(authService.isAuthenticated).mockReturnValue(true)
  vi.mocked(authService.getValidToken).mockRejectedValue(new SessionExpiredError())
  window.history.replaceState(null, '', `/?conversation=${SAVED_ID}`)
  render(<App />)

  await waitFor(() => expect(screen.getByLabelText(/email/i)).toBeInTheDocument())
  expect(conversationInUrl()).toBe(SAVED_ID)
})
