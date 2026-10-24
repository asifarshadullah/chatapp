import { StrictMode } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatWindow } from '../ChatWindow'
import { streamMessage } from '../../services/chatApi'
import type { StreamCallbacks } from '../../services/chatApi'
import { authService } from '../../services/authService'
import {
  SessionExpiredError,
  RenewalFailedError,
  SESSION_EXPIRED_MESSAGE,
  CONNECT_FAILED_MESSAGE,
} from '../../services/sessionErrors'

vi.mock('../../services/chatApi', async (importOriginal) => ({
  // Real constants, so the component's string comparisons are tested for real.
  ...(await importOriginal<typeof import('../../services/chatApi')>()),
  streamMessage: vi.fn(),
}))

const CONVERSATION_ID = 'conv-123'

function mockStream(words: string[], conversationId = CONVERSATION_ID) {
  vi.mocked(streamMessage).mockImplementation(
    async (_msg: string, _convId: string | undefined, callbacks: StreamCallbacks) => {
      callbacks.onConversationId(conversationId)
      words.forEach((w) => callbacks.onWord(w))
      callbacks.onComplete()
    },
  )
}

describe('ChatWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.spyOn(authService, 'getValidToken').mockResolvedValue('token')
  })

  // Cycle 1 — renders ChatInput and MessageList
  it('renders input and message list', () => {
    render(<ChatWindow />)
    expect(screen.getByRole('textbox')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument()
  })

  // Cycle 2 — adds user message to list on send
  it('adds user message to the list immediately on send', async () => {
    mockStream(['Echo: ', 'Hello '])
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    expect(screen.getByText('Hello')).toBeInTheDocument()
  })

  // Cycle 3 — streams assistant response word by word
  it('adds assistant response after streaming completes', async () => {
    mockStream(['Echo: ', 'Hello '])
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByText(/Echo: Hello/)).toBeInTheDocument())
  })

  // Cycle 4 — disables input while streaming
  it('disables input while streaming response', async () => {
    vi.mocked(streamMessage).mockImplementation(
      () => new Promise(() => {}), // never resolves — holds isStreaming=true
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByRole('textbox')).toBeDisabled())
  })

  // Cycle 5 — shows error on streaming failure
  it('shows error message when streaming fails', async () => {
    vi.mocked(streamMessage).mockImplementation(
      async (_msg, _convId, { onError }: StreamCallbacks) => {
        onError('Connection error')
      },
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })

  // Cycle 6 — passes conversationId in subsequent requests
  it('passes conversationId to subsequent sends', async () => {
    const spy = vi.mocked(streamMessage)
    mockStream(['Echo: ', 'response '])
    render(<ChatWindow />)

    await userEvent.type(screen.getByRole('textbox'), 'First')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByText(/Echo:/)).toBeInTheDocument())

    await userEvent.type(screen.getByRole('textbox'), 'Second')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(spy).toHaveBeenCalledTimes(2))

    expect(spy).toHaveBeenNthCalledWith(1, 'First', undefined, expect.any(Object), expect.any(AbortSignal))
    expect(spy).toHaveBeenNthCalledWith(2, 'Second', CONVERSATION_ID, expect.any(Object), expect.any(AbortSignal))
  })

  // ── Cycle 2.2 ──────────────────────────────────────────────────────────

  it('renders streaming words as they arrive', async () => {
    mockStream(['Echo: ', 'Hello ', 'World '])
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello World')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByText(/Echo: Hello World/)).toBeInTheDocument())
  })

  // ── Cycle 2.3 ──────────────────────────────────────────────────────────

  it('shows typing indicator while streaming', async () => {
    vi.mocked(streamMessage).mockImplementation(
      () => new Promise(() => {}), // never resolves — holds isStreaming=true
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() =>
      expect(screen.getByLabelText('typing indicator')).toBeInTheDocument(),
    )
  })

  // ── Cycle 2.4 ──────────────────────────────────────────────────────────

  it('hides typing indicator when streaming completes', async () => {
    mockStream(['Echo: ', 'Hello '])
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() =>
      expect(screen.queryByLabelText('typing indicator')).not.toBeInTheDocument(),
    )
  })

  // ── Cycle 2.5 ──────────────────────────────────────────────────────────

  it('shows error banner when connection fails', async () => {
    vi.mocked(streamMessage).mockImplementation(
      async (_msg, _convId, { onError }: StreamCallbacks) => {
        onError('Connection lost')
      },
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('Connection lost'),
    )
  })

  // ── Cycle FA-CW1 — logout button ─────────────────────────────────────

  it('renders a logout button', () => {
    render(<ChatWindow onLogout={vi.fn()} />)
    expect(screen.getByRole('button', { name: /logout/i })).toBeInTheDocument()
  })

  it('calls onLogout when logout button is clicked', async () => {
    const onLogout = vi.fn()
    render(<ChatWindow onLogout={onLogout} />)
    await userEvent.click(screen.getByRole('button', { name: /logout/i }))
    expect(onLogout).toHaveBeenCalledOnce()
  })

  // ── Cycle FB3.1 — manage billing button ──────────────────────────────

  it('manageBilling_button_inChatWindow_callsOnManageBilling', async () => {
    const onManageBilling = vi.fn()
    render(<ChatWindow onManageBilling={onManageBilling} />)
    await userEvent.click(screen.getByRole('button', { name: /manage plan/i }))
    expect(onManageBilling).toHaveBeenCalledOnce()
  })

  // ── Checking the session when the chat opens ──────────────────────────

  it('renews the token when it opens', async () => {
    render(<ChatWindow />)
    await waitFor(() => expect(authService.getValidToken).toHaveBeenCalled())
  })

  it('reports an ended session on opening, not as a dead server', async () => {
    vi.mocked(authService.getValidToken).mockRejectedValue(new SessionExpiredError())
    render(<ChatWindow />)

    expect(await screen.findByText(SESSION_EXPIRED_MESSAGE)).toBeInTheDocument()
    expect(screen.queryByText(CONNECT_FAILED_MESSAGE)).not.toBeInTheDocument()
  })

  it('notifies the parent so an ended session returns to sign in', async () => {
    vi.mocked(authService.getValidToken).mockRejectedValue(new SessionExpiredError())
    const onSessionExpired = vi.fn()

    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalledOnce())
  })

  it('stays quiet and keeps the session when a renewal on opening merely failed', async () => {
    // Ending the session here would sign out every other tab of it.
    vi.mocked(authService.getValidToken).mockRejectedValue(new RenewalFailedError())
    const onSessionExpired = vi.fn()

    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    await waitFor(() => expect(authService.getValidToken).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 10))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(onSessionExpired).not.toHaveBeenCalled()
  })

  it('shows no error under StrictMode double mounting', async () => {
    render(
      <StrictMode>
        <ChatWindow />
      </StrictMode>,
    )

    await waitFor(() => expect(authService.getValidToken).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 10))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  // ── A reply in flight belongs to the window that asked for it ─────────

  it('aborts the reply in flight when it unmounts', async () => {
    let signal: AbortSignal | undefined
    vi.mocked(streamMessage).mockImplementation((_msg, _convId, _callbacks, s) => {
      signal = s
      return new Promise(() => {})
    })
    const { unmount } = render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(signal).toBeDefined())

    unmount()

    expect(signal!.aborted).toBe(true)
  })

  it('notifies the parent when a send fails on an expired session', async () => {
    vi.mocked(streamMessage).mockImplementation(
      async (_msg: string, _convId: string | undefined, callbacks: StreamCallbacks) => {
        callbacks.onError(SESSION_EXPIRED_MESSAGE)
      },
    )
    const onSessionExpired = vi.fn()
    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalledOnce())
  })

  it('does not treat an ordinary stream error as an expired session', async () => {
    vi.mocked(streamMessage).mockImplementation(
      async (_msg: string, _convId: string | undefined, callbacks: StreamCallbacks) => {
        callbacks.onError('Connection error')
      },
    )
    const onSessionExpired = vi.fn()
    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))

    expect(await screen.findByText('Connection error')).toBeInTheDocument()
    expect(onSessionExpired).not.toHaveBeenCalled()
  })
})
