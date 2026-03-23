import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LoginPage } from '../LoginPage'
import { authService } from '../../services/authService'

vi.mock('../../services/authService', () => ({
  authService: {
    login: vi.fn(),
    register: vi.fn(),
  },
}))

beforeEach(() => {
  vi.clearAllMocks()
})

// ── Cycle FA2.1 ──────────────────────────────────────────────────────────────

it('renders email, password inputs and a submit button', () => {
  render(<LoginPage onLogin={vi.fn()} />)
  expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  expect(screen.getByLabelText(/password/i)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /login/i })).toBeInTheDocument()
})

it('renders a toggle to switch to register mode', () => {
  render(<LoginPage onLogin={vi.fn()} />)
  expect(screen.getByRole('button', { name: /register/i })).toBeInTheDocument()
})

// ── Cycle FA2.2 ──────────────────────────────────────────────────────────────

it('submit in login mode calls authService.login and then onLogin', async () => {
  vi.mocked(authService.login).mockResolvedValue({
    accessToken: 'tok',
    expiresAt: '',
    userId: 'uid',
  })
  const onLogin = vi.fn()
  render(<LoginPage onLogin={onLogin} />)

  await userEvent.type(screen.getByLabelText(/email/i), 'user@test.com')
  await userEvent.type(screen.getByLabelText(/password/i), 'Password123')
  await userEvent.click(screen.getByRole('button', { name: /login/i }))

  await waitFor(() => {
    expect(authService.login).toHaveBeenCalledWith('user@test.com', 'Password123')
    expect(onLogin).toHaveBeenCalled()
  })
})

// ── Cycle FA2.3 ──────────────────────────────────────────────────────────────

it('submit in register mode calls authService.register and then onLogin', async () => {
  vi.mocked(authService.register).mockResolvedValue({
    accessToken: 'tok',
    expiresAt: '',
    userId: 'uid',
  })
  const onLogin = vi.fn()
  render(<LoginPage onLogin={onLogin} />)

  // Switch to register mode
  await userEvent.click(screen.getByRole('button', { name: /register/i }))

  await userEvent.type(screen.getByLabelText(/email/i), 'new@test.com')
  await userEvent.type(screen.getByLabelText(/password/i), 'Password123')
  await userEvent.type(screen.getByLabelText(/display name/i), 'Alice')
  await userEvent.click(screen.getByRole('button', { name: /register/i }))

  await waitFor(() => {
    expect(authService.register).toHaveBeenCalledWith('new@test.com', 'Password123', 'Alice')
    expect(onLogin).toHaveBeenCalled()
  })
})

// ── Cycle FA2.4 ──────────────────────────────────────────────────────────────

it('shows error alert when login fails', async () => {
  vi.mocked(authService.login).mockRejectedValue(new Error('401'))
  render(<LoginPage onLogin={vi.fn()} />)

  await userEvent.type(screen.getByLabelText(/email/i), 'bad@test.com')
  await userEvent.type(screen.getByLabelText(/password/i), 'wrong')
  await userEvent.click(screen.getByRole('button', { name: /login/i }))

  await waitFor(() => {
    expect(screen.getByRole('alert')).toBeInTheDocument()
  })
})
