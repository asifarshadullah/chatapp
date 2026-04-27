import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatWindow } from '../ChatWindow'
import { signalRService } from '../../services/signalRService'
import type { StreamCallbacks } from '../../services/signalRService'

vi.mock('../../services/signalRService', () => ({
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
})
