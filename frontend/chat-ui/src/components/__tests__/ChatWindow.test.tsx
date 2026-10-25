import { StrictMode } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatWindow } from '../ChatWindow'
import {
  streamMessage,
  getHistory,
  getModels,
  CONVERSATION_NOT_FOUND_MESSAGE,
  HISTORY_LOAD_FAILED_MESSAGE,
  MODELS_LOAD_FAILED_MESSAGE,
} from '../../services/chatApi'
import type { HistoryResult, ModelsResult, StreamCallbacks } from '../../services/chatApi'
import type { ChatMessage } from '../../types/chat'
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
  getHistory: vi.fn(),
  getModels: vi.fn(),
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

const LOCAL_ONLY: ModelsResult = { defaultModel: 'local', models: [{ id: 'local', name: 'Local · gemma3:4b' }] }
const LOCAL_AND_GROQ: ModelsResult = {
  defaultModel: 'local',
  models: [{ id: 'local', name: 'Local · gemma3:4b' }, { id: 'groq', name: 'Groq · gpt-oss-120b' }],
}

const SAVED_ID = '3f2c1d4e-5a6b-4c7d-8e9f-0a1b2c3d4e5f'
const SAVED_MESSAGES: ChatMessage[] = [
  { id: 'm1', message: 'Earlier question', role: 'user', timestamp: '2026-10-25T10:00:00Z' },
  { id: 'm2', message: 'Earlier answer', role: 'assistant', timestamp: '2026-10-25T10:00:01Z' },
]

function openAt(url: string) {
  window.history.replaceState(null, '', url)
}

function conversationInUrl() {
  return new URLSearchParams(window.location.search).get('conversation')
}

async function send(text: string) {
  await userEvent.type(screen.getByRole('textbox'), text)
  await userEvent.click(screen.getByRole('button', { name: /send/i }))
}

describe('ChatWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    openAt('/')
    localStorage.clear()
    vi.mocked(getHistory).mockResolvedValue({ messages: SAVED_MESSAGES })
    vi.mocked(getModels).mockResolvedValue(LOCAL_ONLY)
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

    expect(spy).toHaveBeenNthCalledWith(1, 'First', undefined, expect.any(Object), expect.any(AbortSignal), 'local')
    expect(spy).toHaveBeenNthCalledWith(2, 'Second', CONVERSATION_ID, expect.any(Object), expect.any(AbortSignal), 'local')
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

  // ── The open conversation in the URL ──────────────────────────────────

  it('names a new conversation in the URL without adding a history entry', async () => {
    openAt('/?ref=mail#latest')
    const entries = window.history.length
    mockStream(['Hi'])
    render(<ChatWindow />)

    await send('Hello')

    await waitFor(() => expect(conversationInUrl()).toBe(CONVERSATION_ID))
    expect(new URLSearchParams(window.location.search).get('ref')).toBe('mail')
    expect(window.location.hash).toBe('#latest')
    expect(window.history.length).toBe(entries)
  })

  it('keeps naming the conversation after a second message', async () => {
    mockStream(['Hi'])
    render(<ChatWindow />)

    await send('First')
    await waitFor(() => expect(screen.getByText('Hi')).toBeInTheDocument())
    await send('Second')

    await waitFor(() => expect(streamMessage).toHaveBeenCalledTimes(2))
    expect(window.location.search).toBe(`?conversation=${CONVERSATION_ID}`)
  })

  // ── Reopening the conversation named in the URL ───────────────────────

  it('shows the saved messages of the conversation in the URL, in order', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    render(<ChatWindow />)

    const question = await screen.findByText('Earlier question')
    const answer = screen.getByText('Earlier answer')
    expect(question.compareDocumentPosition(answer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(getHistory).toHaveBeenCalledWith(SAVED_ID)
  })

  it('shows that it is loading, and blocks sending, until the history arrives', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    let finish!: (result: HistoryResult) => void
    vi.mocked(getHistory).mockReturnValue(new Promise((resolve) => { finish = resolve }))
    render(<ChatWindow />)

    expect(screen.getByLabelText('loading conversation')).toBeInTheDocument()
    expect(screen.queryByText(/what's on the agenda/i)).not.toBeInTheDocument()
    expect(screen.getByRole('textbox')).toBeDisabled()

    finish({ messages: SAVED_MESSAGES })

    await waitFor(() => expect(screen.getByRole('textbox')).toBeEnabled())
    expect(screen.queryByLabelText('loading conversation')).not.toBeInTheDocument()
  })

  it('reopens the conversation when it mounts again, as after the billing page', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    const { unmount } = render(<ChatWindow />)
    await screen.findByText('Earlier question')

    unmount()
    render(<ChatWindow />)

    expect(await screen.findByText('Earlier question')).toBeInTheDocument()
  })

  it('continues the reopened conversation', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    mockStream(['Hi'], SAVED_ID)
    render(<ChatWindow />)
    await screen.findByText('Earlier question')

    await send('Follow-up')

    await waitFor(() =>
      expect(streamMessage).toHaveBeenCalledWith('Follow-up', SAVED_ID, expect.any(Object), expect.any(AbortSignal), 'local'),
    )
  })

  it('shows each saved message once under StrictMode', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    render(
      <StrictMode>
        <ChatWindow />
      </StrictMode>,
    )

    await screen.findByText('Earlier question')
    await new Promise((r) => setTimeout(r, 10))
    expect(screen.getAllByText('Earlier question')).toHaveLength(1)
  })

  // ── A conversation that cannot be reopened ────────────────────────────

  it('reports a conversation it cannot open as not found and starts afresh', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    vi.mocked(getHistory).mockResolvedValue({ error: CONVERSATION_NOT_FOUND_MESSAGE })
    mockStream(['Hi'])
    render(<ChatWindow />)

    expect(await screen.findByText(CONVERSATION_NOT_FOUND_MESSAGE)).toBeInTheDocument()
    expect(conversationInUrl()).toBeNull()

    await send('Hello')
    await waitFor(() =>
      expect(streamMessage).toHaveBeenCalledWith('Hello', undefined, expect.any(Object), expect.any(AbortSignal), 'local'),
    )
  })

  it.each([
    ['a relative path', '..%2F..%2Fauth%2Fsessions%3Fx%3D'],
    ['escaped dots', '%2e%2e'],
    ['a plain word', 'abc'],
  ])('treats %s as not found without asking the server', async (_label, value) => {
    openAt(`/?conversation=${value}`)
    render(<ChatWindow />)

    expect(await screen.findByText(CONVERSATION_NOT_FOUND_MESSAGE)).toBeInTheDocument()
    expect(getHistory).not.toHaveBeenCalled()
    expect(conversationInUrl()).toBeNull()
  })

  it('keeps the conversation in the URL, and blocks sending, when its history fails to load', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    vi.mocked(getHistory).mockResolvedValue({ error: HISTORY_LOAD_FAILED_MESSAGE })
    render(<ChatWindow />)

    expect(await screen.findByText(HISTORY_LOAD_FAILED_MESSAGE)).toBeInTheDocument()
    expect(conversationInUrl()).toBe(SAVED_ID)
    expect(screen.getByRole('textbox')).toBeDisabled()
  })

  it('returns to sign in, keeping the conversation in the URL, when the session has ended', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    vi.mocked(getHistory).mockResolvedValue({ error: SESSION_EXPIRED_MESSAGE })
    const onSessionExpired = vi.fn()
    render(<ChatWindow onSessionExpired={onSessionExpired} />)

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalled())
    expect(conversationInUrl()).toBe(SAVED_ID)
  })

  // ── New chat ──────────────────────────────────────────────────────────

  it('offers no new chat on an empty chat', () => {
    render(<ChatWindow />)
    expect(screen.queryByRole('button', { name: /new chat/i })).not.toBeInTheDocument()
  })

  it('does not allow a new chat while a reply streams', async () => {
    vi.mocked(streamMessage).mockImplementation(() => new Promise(() => {}))
    render(<ChatWindow />)

    await send('Hello')

    await waitFor(() => expect(screen.getByRole('button', { name: /new chat/i })).toBeDisabled())
  })

  it('starts an empty conversation on new chat', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    mockStream(['Hi'])
    render(<ChatWindow />)
    await screen.findByText('Earlier question')

    await userEvent.click(screen.getByRole('button', { name: /new chat/i }))

    expect(screen.queryByText('Earlier question')).not.toBeInTheDocument()
    expect(conversationInUrl()).toBeNull()
    await send('Hello')
    await waitFor(() =>
      expect(streamMessage).toHaveBeenCalledWith('Hello', undefined, expect.any(Object), expect.any(AbortSignal), 'local'),
    )
  })

  it('clears an error on new chat', async () => {
    vi.mocked(streamMessage).mockImplementation(async (_msg, _convId, { onError }: StreamCallbacks) => {
      onError('Connection error')
    })
    render(<ChatWindow />)
    await send('Hello')
    await screen.findByText('Connection error')

    await userEvent.click(screen.getByRole('button', { name: /new chat/i }))

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('leaves a conversation that failed to load on new chat', async () => {
    openAt(`/?conversation=${SAVED_ID}`)
    vi.mocked(getHistory).mockResolvedValue({ error: HISTORY_LOAD_FAILED_MESSAGE })
    render(<ChatWindow />)
    await screen.findByText(HISTORY_LOAD_FAILED_MESSAGE)

    await userEvent.click(screen.getByRole('button', { name: /new chat/i }))

    expect(screen.getByRole('textbox')).toBeEnabled()
    expect(conversationInUrl()).toBeNull()
  })

  // ── Settings: the model ─────────────────────────────────────────────────

  async function openSettings() {
    await userEvent.click(screen.getByRole('button', { name: 'Settings' }))
    return screen.findByRole('dialog', { name: 'Settings' })
  }

  async function chooseModel(dialog: HTMLElement, name: string) {
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Model' }))
    await userEvent.click(await screen.findByRole('option', { name }))
  }

  it('lists the offered models in Settings, with the one in use selected', async () => {
    vi.mocked(getModels).mockResolvedValue(LOCAL_AND_GROQ)
    render(<ChatWindow />)

    const dialog = await openSettings()

    const model = within(dialog).getByRole('combobox', { name: 'Model' })
    await waitFor(() => expect(model).toHaveTextContent('Local · gemma3:4b'))
    await userEvent.click(model)
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Local · gemma3:4b', 'Groq · gpt-oss-120b'])
  })

  it('shows the model even when only one is offered', async () => {
    render(<ChatWindow />)

    const dialog = await openSettings()

    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Model' })).toHaveTextContent('Local · gemma3:4b'))
  })

  it('sends the next message with the model saved in Settings', async () => {
    vi.mocked(getModels).mockResolvedValue(LOCAL_AND_GROQ)
    mockStream(['Hi'])
    render(<ChatWindow />)

    const dialog = await openSettings()
    await chooseModel(dialog, 'Groq · gpt-oss-120b')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await send('Hello')

    await waitFor(() =>
      expect(streamMessage).toHaveBeenCalledWith('Hello', undefined, expect.any(Object), expect.any(AbortSignal), 'groq'),
    )
  })

  it('keeps the model in use when Settings is cancelled', async () => {
    vi.mocked(getModels).mockResolvedValue(LOCAL_AND_GROQ)
    mockStream(['Hi'])
    render(<ChatWindow />)

    const dialog = await openSettings()
    await chooseModel(dialog, 'Groq · gpt-oss-120b')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await send('Hello')

    await waitFor(() =>
      expect(streamMessage).toHaveBeenCalledWith('Hello', undefined, expect.any(Object), expect.any(AbortSignal), 'local'),
    )
  })

  it('remembers the saved model across a remount', async () => {
    vi.mocked(getModels).mockResolvedValue(LOCAL_AND_GROQ)
    const { unmount } = render(<ChatWindow />)
    const dialog = await openSettings()
    await chooseModel(dialog, 'Groq · gpt-oss-120b')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))
    unmount()

    render(<ChatWindow />)
    const reopened = await openSettings()

    await waitFor(() =>
      expect(within(reopened).getByRole('combobox', { name: 'Model' })).toHaveTextContent('Groq · gpt-oss-120b'),
    )
  })

  it('says the models could not be loaded, and sends without naming one', async () => {
    vi.mocked(getModels).mockResolvedValue({ error: MODELS_LOAD_FAILED_MESSAGE })
    mockStream(['Hi'])
    render(<ChatWindow />)

    const dialog = await openSettings()
    expect(await within(dialog).findByText(MODELS_LOAD_FAILED_MESSAGE)).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await send('Hello')

    await waitFor(() => expect(streamMessage).toHaveBeenCalled())
    expect(vi.mocked(streamMessage).mock.calls[0][4]).toBeUndefined()
  })
})
