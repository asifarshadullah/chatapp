import { StrictMode } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatWindow } from '../ChatWindow'
import {
  signalRService,
  SessionExpiredError,
  SESSION_EXPIRED_MESSAGE,
  CONNECT_FAILED_MESSAGE,
} from '../../services/signalRService'
import type { StreamCallbacks } from '../../services/signalRService'

import { RenewalFailedError } from '../../services/sessionErrors'

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

const CONVERSATION_ID = 'conv-123'

function mockStream(words: string[], conversationId = CONVERSATION_ID) {
  vi.mocked(signalRService.sendMessage).mockImplementation(
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
    vi.mocked(signalRService.start).mockResolvedValue(undefined)
    vi.mocked(signalRService.stop).mockResolvedValue(undefined)
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
    vi.mocked(signalRService.sendMessage).mockImplementation(
      () => new Promise(() => {}), // never resolves — holds isStreaming=true
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByRole('textbox')).toBeDisabled())
  })

  // Cycle 5 — shows error on streaming failure
  it('shows error message when streaming fails', async () => {
    vi.mocked(signalRService.sendMessage).mockImplementation(
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
  it('passes conversationId to subsequent sendMessage calls', async () => {
    const spy = vi.mocked(signalRService.sendMessage)
    mockStream(['Echo: ', 'response '])
    render(<ChatWindow />)

    await userEvent.type(screen.getByRole('textbox'), 'First')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByText(/Echo:/)).toBeInTheDocument())

    await userEvent.type(screen.getByRole('textbox'), 'Second')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(spy).toHaveBeenCalledTimes(2))

    expect(spy).toHaveBeenNthCalledWith(1, 'First', undefined, expect.any(Object))
    expect(spy).toHaveBeenNthCalledWith(2, 'Second', CONVERSATION_ID, expect.any(Object))
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
    vi.mocked(signalRService.sendMessage).mockImplementation(
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
    vi.mocked(signalRService.sendMessage).mockImplementation(
      async (_msg, _convId, { onError }: StreamCallbacks) => {
        onError('SignalR connection lost')
      },
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('SignalR connection lost'),
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

  // ── Connection lifecycle regressions ──────────────────────────────────
  //
  // The chat page once showed "Failed to connect to chat server." on every
  // load in dev while the hub was perfectly healthy: StrictMode's remount
  // raced the unmount cleanup, which stopped the connection the remount was
  // waiting on. These pin the contract that prevents it recurring.

  it('does not stop the shared connection when it unmounts', () => {
    const { unmount } = render(<ChatWindow />)
    unmount()
    // The connection is a module singleton meant to outlive one component.
    // Stopping it here aborts a connect a remount may already be awaiting.
    expect(signalRService.stop).not.toHaveBeenCalled()
  })

  it('shows no connection error under StrictMode double mounting', async () => {
    // The mock mirrors the real client on the one point that matters here:
    // stopping a connection mid-negotiation rejects the pending start. Without
    // that, a mocked start() always resolves and the race cannot be observed.
    let rejectPending: ((e: Error) => void) | null = null
    vi.mocked(signalRService.start).mockImplementation(
      () => new Promise<void>((resolve, reject) => {
        rejectPending = reject
        setTimeout(() => { rejectPending = null; resolve() }, 0)
      }),
    )
    vi.mocked(signalRService.stop).mockImplementation(async () => {
      rejectPending?.(new Error('The connection was stopped during negotiation.'))
    })

    render(
      <StrictMode>
        <ChatWindow />
      </StrictMode>,
    )

    await waitFor(() => expect(signalRService.start).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 10))
    expect(screen.queryByText(CONNECT_FAILED_MESSAGE)).not.toBeInTheDocument()
  })

  it('shows the connection error when the connect genuinely fails', async () => {
    vi.mocked(signalRService.start).mockRejectedValue(new Error('transport failure'))
    render(<ChatWindow />)
    expect(await screen.findByText(CONNECT_FAILED_MESSAGE)).toBeInTheDocument()
  })

  // ── Expired session must not read as an unreachable server ────────────

  it('reports an expired session distinctly, not as a dead server', async () => {
    vi.mocked(signalRService.start).mockRejectedValue(new SessionExpiredError())
    render(<ChatWindow />)

    expect(await screen.findByText(SESSION_EXPIRED_MESSAGE)).toBeInTheDocument()
    expect(screen.queryByText(CONNECT_FAILED_MESSAGE)).not.toBeInTheDocument()
  })

  it('does not end the session when a renewal merely failed', async () => {
    // The parent's handler for an ended session revokes the refresh credential server-side,
    // which would sign out every other tab of this session. A renewal that lost a race to a
    // sibling must therefore reach the user as an ordinary transient failure.
    vi.mocked(signalRService.start).mockRejectedValue(new RenewalFailedError())
    const onSessionExpired = vi.fn()

    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    expect(await screen.findByText(CONNECT_FAILED_MESSAGE)).toBeInTheDocument()
    expect(screen.queryByText(SESSION_EXPIRED_MESSAGE)).not.toBeInTheDocument()
    expect(onSessionExpired).not.toHaveBeenCalled()
  })

  it('notifies the parent so an expired session returns to sign in', async () => {
    vi.mocked(signalRService.start).mockRejectedValue(new SessionExpiredError())
    const onSessionExpired = vi.fn()

    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalledOnce())
  })

  it('notifies the parent when a send fails on an expired session', async () => {
    vi.mocked(signalRService.sendMessage).mockImplementation(
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
    vi.mocked(signalRService.sendMessage).mockImplementation(
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
