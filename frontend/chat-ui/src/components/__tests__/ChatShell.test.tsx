import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatShell } from '../ChatShell'
import {
  streamMessage,
  getHistory,
  getModels,
  listConversations,
  HISTORY_LOAD_FAILED_MESSAGE,
} from '../../services/chatApi'
import type { ConversationPageResult, ConversationSummary, StreamCallbacks } from '../../services/chatApi'
import type { ChatMessage } from '../../types/chat'
import { authService } from '../../services/authService'
import { SESSION_EXPIRED_MESSAGE } from '../../services/sessionErrors'

vi.mock('../../services/chatApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../services/chatApi')>()),
  streamMessage: vi.fn(),
  getHistory: vi.fn(),
  getModels: vi.fn(),
  listConversations: vi.fn(),
}))

const FIRST_ID = '11111111-1111-4111-8111-111111111111'
const SECOND_ID = '22222222-2222-4222-8222-222222222222'
const NEW_ID = '33333333-3333-4333-8333-333333333333'

const FIRST: ConversationSummary = { id: FIRST_ID, title: 'About monads', lastActivityAt: '2026-10-26T10:00:00Z' }
const SECOND: ConversationSummary = { id: SECOND_ID, title: 'About functors', lastActivityAt: '2026-10-25T10:00:00Z' }

const HISTORIES: Record<string, ChatMessage[]> = {
  [FIRST_ID]: [
    { id: 'f1', message: 'What is a monad?', role: 'user', timestamp: '2026-10-26T09:59:59Z' },
    { id: 'f2', message: 'A monad is…', role: 'assistant', timestamp: '2026-10-26T10:00:00Z' },
  ],
  [SECOND_ID]: [
    { id: 's1', message: 'What is a functor?', role: 'user', timestamp: '2026-10-25T09:59:59Z' },
    { id: 's2', message: 'A functor is…', role: 'assistant', timestamp: '2026-10-25T10:00:00Z' },
  ],
}

function page(conversations: ConversationSummary[], nextCursor: string | null = null): ConversationPageResult {
  return { conversations, nextCursor }
}

/** Makes MUI's media queries answer as a window of the given width would. */
function atWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => {
    const min = /min-width:\s*([\d.]+)px/.exec(query)
    const max = /max-width:\s*([\d.]+)px/.exec(query)
    const matches = (!min || width >= Number(min[1])) && (!max || width <= Number(max[1]))
    return {
      matches,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }
  })
}

function openAt(url: string) {
  window.history.replaceState(null, '', url)
}

function conversationInUrl() {
  return new URLSearchParams(window.location.search).get('conversation')
}

function sidebarList() {
  return screen.getByRole('list', { name: 'Conversations' })
}

async function send(text: string) {
  await userEvent.type(screen.getByRole('textbox'), text)
  await userEvent.click(screen.getByRole('button', { name: /send/i }))
}

function replyWith(conversationId: string, words: string[]) {
  vi.mocked(streamMessage).mockImplementation(
    async (_msg: string, _convId: string | undefined, callbacks: StreamCallbacks) => {
      callbacks.onConversationId(conversationId)
      words.forEach((w) => callbacks.onWord(w))
      callbacks.onComplete()
    },
  )
}

describe('ChatShell', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    openAt('/')
    localStorage.clear()
    atWidth(1280)
    vi.spyOn(authService, 'getValidToken').mockResolvedValue('token')
    vi.mocked(getModels).mockResolvedValue({ defaultModel: 'local', models: [{ id: 'local', name: 'Local · gemma3:4b' }] })
    vi.mocked(getHistory).mockImplementation(async (id: string) =>
      id in HISTORIES ? { messages: HISTORIES[id] } : { error: 'Conversation not found.' },
    )
    vi.mocked(listConversations).mockResolvedValue(page([FIRST, SECOND]))
  })

  afterEach(() => vi.unstubAllGlobals())

  // ── Layout ────────────────────────────────────────────────────────────────

  it('shows the sidebar beside the chat on a wide window, with no menu button', async () => {
    render(<ChatShell />)

    expect(await within(await screen.findByRole('list', { name: 'Conversations' })).findByText('About monads')).toBeInTheDocument()
    expect(screen.getByRole('textbox')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open conversations' })).not.toBeInTheDocument()
  })

  it('hides the sidebar behind a menu button on a narrow window', async () => {
    atWidth(390)
    render(<ChatShell />)

    expect(screen.queryByRole('list', { name: 'Conversations' })).not.toBeInTheDocument()
    await userEvent.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Open conversations' }))

    expect(await within(await screen.findByRole('list', { name: 'Conversations' })).findByText('About monads')).toBeInTheDocument()
  })

  // ── Choosing a conversation ──────────────────────────────────────────────

  it('opens a chosen conversation like its URL, without a history entry, and continues it', async () => {
    replyWith(SECOND_ID, ['Sure'])
    openAt(`/?conversation=${FIRST_ID}`)
    render(<ChatShell />)
    await screen.findByText('What is a monad?')
    const historyLength = window.history.length

    await userEvent.click(within(sidebarList()).getByRole('button', { name: 'About functors' }))

    expect(await screen.findByText('What is a functor?')).toBeInTheDocument()
    expect(screen.queryByText('What is a monad?')).not.toBeInTheDocument()
    expect(conversationInUrl()).toBe(SECOND_ID)
    expect(window.history.length).toBe(historyLength)
    await waitFor(() =>
      expect(within(sidebarList()).getByRole('button', { name: 'About functors' })).toHaveAttribute('aria-current', 'page'),
    )

    await send('And applicatives?')
    await waitFor(() => expect(streamMessage).toHaveBeenCalledWith(
      'And applicatives?', SECOND_ID, expect.any(Object), expect.any(AbortSignal), 'local',
    ))
  })

  it('leaves the chat as it is when the open conversation is chosen', async () => {
    openAt(`/?conversation=${FIRST_ID}`)
    render(<ChatShell />)
    await screen.findByText('What is a monad?')
    await waitFor(() =>
      expect(within(sidebarList()).getByRole('button', { name: 'About monads' })).toHaveAttribute('aria-current', 'page'),
    )

    await userEvent.click(within(sidebarList()).getByRole('button', { name: 'About monads' }))

    expect(getHistory).toHaveBeenCalledTimes(1)
    expect(screen.getByText('What is a monad?')).toBeInTheDocument()
  })

  it('closes the sidebar on a narrow window when a conversation is chosen', async () => {
    atWidth(390)
    render(<ChatShell />)
    await userEvent.click(screen.getByRole('button', { name: 'Open conversations' }))

    await userEvent.click(await within(await screen.findByRole('list', { name: 'Conversations' })).findByRole('button', { name: 'About monads' }))

    expect(await screen.findByText('What is a monad?')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('list', { name: 'Conversations' })).not.toBeInTheDocument())
  })

  // ── New chat ──────────────────────────────────────────────────────────────

  it('shows New chat disabled on an empty chat', async () => {
    render(<ChatShell />)

    await within(sidebarListWhenLoaded()).findByText('About monads')
    expect(screen.getByRole('button', { name: 'New chat' })).toBeDisabled()
  })

  it('does not allow a new chat while a reply streams', async () => {
    vi.mocked(streamMessage).mockImplementation(() => new Promise(() => {}))
    render(<ChatShell />)

    await send('Hello')

    await waitFor(() => expect(screen.getByText('Assistant is typing…')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'New chat' })).toBeDisabled()
    expect(within(sidebarList()).getByRole('button', { name: 'About monads' })).toHaveAttribute('aria-disabled', 'true')
  })

  it('starts an empty conversation on New chat', async () => {
    openAt(`/?conversation=${FIRST_ID}`)
    replyWith(NEW_ID, ['Hi'])
    render(<ChatShell />)
    await screen.findByText('What is a monad?')

    await userEvent.click(screen.getByRole('button', { name: 'New chat' }))

    expect(screen.queryByText('What is a monad?')).not.toBeInTheDocument()
    expect(conversationInUrl()).toBeNull()
    await send('Hello')
    await waitFor(() =>
      expect(streamMessage).toHaveBeenCalledWith('Hello', undefined, expect.any(Object), expect.any(AbortSignal), 'local'),
    )
  })

  it('clears an error on New chat', async () => {
    vi.mocked(streamMessage).mockImplementation(async (_msg, _convId, { onError }: StreamCallbacks) => {
      onError('Connection error')
    })
    render(<ChatShell />)
    await send('Hello')
    await screen.findByText('Connection error')

    await userEvent.click(screen.getByRole('button', { name: 'New chat' }))

    expect(screen.queryByText('Connection error')).not.toBeInTheDocument()
  })

  it('leaves a conversation that failed to load on New chat', async () => {
    openAt(`/?conversation=${FIRST_ID}`)
    vi.mocked(getHistory).mockResolvedValue({ error: HISTORY_LOAD_FAILED_MESSAGE })
    render(<ChatShell />)
    await screen.findByText(HISTORY_LOAD_FAILED_MESSAGE)

    await userEvent.click(screen.getByRole('button', { name: 'New chat' }))

    expect(screen.getByRole('textbox')).toBeEnabled()
    expect(conversationInUrl()).toBeNull()
  })

  it('closes the sidebar on a narrow window when New chat is chosen', async () => {
    atWidth(390)
    openAt(`/?conversation=${FIRST_ID}`)
    render(<ChatShell />)
    await screen.findByText('What is a monad?')
    await userEvent.click(screen.getByRole('button', { name: 'Open conversations' }))

    await userEvent.click(await screen.findByRole('button', { name: 'New chat' }))

    expect(conversationInUrl()).toBeNull()
    await waitFor(() => expect(screen.queryByRole('list', { name: 'Conversations' })).not.toBeInTheDocument())
  })

  // ── Following activity ────────────────────────────────────────────────────

  it('reloads the list when a reply ends, and marks the new conversation as current', async () => {
    const created: ConversationSummary = { id: NEW_ID, title: 'Greetings', lastActivityAt: '2026-10-27T10:00:00Z' }
    replyWith(NEW_ID, ['Hi'])
    render(<ChatShell />)
    await within(sidebarListWhenLoaded()).findByText('About monads')
    vi.mocked(listConversations).mockResolvedValue(page([created, FIRST, SECOND]))

    await send('Hello')

    const entry = await within(sidebarList()).findByRole('button', { name: 'Greetings' })
    expect(entry).toHaveAttribute('aria-current', 'page')
    expect(within(sidebarList()).getAllByRole('button')[0]).toBe(entry)
  })

  it('reloads the list when a send ends in an error', async () => {
    vi.mocked(streamMessage).mockImplementation(async (_msg, _convId, { onError }: StreamCallbacks) => {
      onError('The reply could not be completed.')
    })
    render(<ChatShell />)
    await within(sidebarListWhenLoaded()).findByText('About monads')

    await send('Hello')

    await waitFor(() => expect(listConversations).toHaveBeenCalledTimes(2))
  })

  it('sends an ended session to sign in when the list finds it', async () => {
    vi.mocked(listConversations).mockResolvedValue({ error: SESSION_EXPIRED_MESSAGE })
    const onSessionExpired = vi.fn()

    render(<ChatShell onSessionExpired={onSessionExpired} />)

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalled())
  })
})

function sidebarListWhenLoaded() {
  return screen.getByRole('navigation', { name: 'Saved conversations' })
}
