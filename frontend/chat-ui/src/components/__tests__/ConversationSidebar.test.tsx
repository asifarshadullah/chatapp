import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConversationSidebar } from '../ConversationSidebar'
import { listConversations, CONVERSATIONS_LOAD_FAILED_MESSAGE } from '../../services/chatApi'
import type { ConversationPageResult, ConversationSummary } from '../../services/chatApi'
import { SESSION_EXPIRED_MESSAGE } from '../../services/sessionErrors'

vi.mock('../../services/chatApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../services/chatApi')>()),
  listConversations: vi.fn(),
}))

const MORE_FAILED_MESSAGE = 'More conversations could not be loaded.'

function entry(n: number): ConversationSummary {
  return { id: `c${n}`, title: `Conversation ${n}`, lastActivityAt: `2026-10-${String(n).padStart(2, '0')}T10:00:00Z` }
}

/** Entries numbered from `from` down to `to`, newest first. */
function entries(from: number, to: number): ConversationSummary[] {
  const list: ConversationSummary[] = []
  for (let n = from; n >= to; n--) list.push(entry(n))
  return list
}

function page(conversations: ConversationSummary[], nextCursor: string | null = null): ConversationPageResult {
  return { conversations, nextCursor }
}

function titles() {
  return within(screen.getByRole('list', { name: 'Conversations' }))
    .getAllByRole('button')
    .map((b) => b.textContent)
}

type Props = Partial<Parameters<typeof ConversationSidebar>[0]>

function renderSidebar(props: Props = {}) {
  const all = {
    currentId: undefined,
    busy: false,
    canStartNewChat: true,
    refreshKey: 0,
    onOpen: vi.fn(),
    onNewChat: vi.fn(),
    onSessionExpired: vi.fn(),
    ...props,
  }
  const view = render(<ConversationSidebar {...all} />)
  return { ...all, rerender: (next: Props) => view.rerender(<ConversationSidebar {...all} {...next} />) }
}

describe('ConversationSidebar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ── Showing the list ──────────────────────────────────────────────────────

  it('shows that it is loading, then the titles in the order given', async () => {
    let answer!: (result: ConversationPageResult) => void
    vi.mocked(listConversations).mockReturnValue(new Promise((resolve) => (answer = resolve)))

    renderSidebar()

    expect(screen.getByLabelText('loading conversations')).toBeInTheDocument()
    answer(page([entry(2), entry(1)]))
    expect(await screen.findByText('Conversation 2')).toBeInTheDocument()
    expect(titles()).toEqual(['Conversation 2', 'Conversation 1'])
    expect(screen.queryByLabelText('loading conversations')).not.toBeInTheDocument()
  })

  it('says so when there are no conversations', async () => {
    vi.mocked(listConversations).mockResolvedValue(page([]))

    renderSidebar()

    expect(await screen.findByText('No conversations yet.')).toBeInTheDocument()
  })

  it('shows a title that looks like markup as plain text', async () => {
    vi.mocked(listConversations).mockResolvedValue(
      page([{ id: 'c1', title: '<b>bold</b>', lastActivityAt: '2026-10-01T10:00:00Z' }]),
    )

    const { container } = render(
      <ConversationSidebar
        busy={false}
        canStartNewChat
        refreshKey={0}
        onOpen={vi.fn()}
        onNewChat={vi.fn()}
        onSessionExpired={vi.fn()}
      />,
    )

    expect(await screen.findByText('<b>bold</b>')).toBeInTheDocument()
    expect(container.querySelector('b')).toBeNull()
  })

  it('marks the open conversation as the current one', async () => {
    vi.mocked(listConversations).mockResolvedValue(page([entry(2), entry(1)]))

    renderSidebar({ currentId: 'c1' })

    const current = await screen.findByRole('button', { name: 'Conversation 1' })
    expect(current).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Conversation 2' })).not.toHaveAttribute('aria-current')
  })

  // ── Failures ──────────────────────────────────────────────────────────────

  it('offers Retry when the list cannot be loaded, and Retry loads it', async () => {
    vi.mocked(listConversations)
      .mockResolvedValueOnce({ error: CONVERSATIONS_LOAD_FAILED_MESSAGE })
      .mockResolvedValueOnce(page([entry(1)]))

    renderSidebar()

    expect(await screen.findByText(CONVERSATIONS_LOAD_FAILED_MESSAGE)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('Conversation 1')).toBeInTheDocument()
    expect(screen.queryByText(CONVERSATIONS_LOAD_FAILED_MESSAGE)).not.toBeInTheDocument()
  })

  it('sends an ended session to sign in', async () => {
    vi.mocked(listConversations).mockResolvedValue({ error: SESSION_EXPIRED_MESSAGE })

    const { onSessionExpired } = renderSidebar()

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalled())
  })

  // ── Show more ─────────────────────────────────────────────────────────────

  it('adds the next page on Show more, each conversation once, and stops offering it on the last page', async () => {
    vi.mocked(listConversations)
      .mockResolvedValueOnce(page(entries(25, 6), 'cursor-1'))
      // The next page repeats one entry, as it can when a conversation moved between requests.
      .mockResolvedValueOnce(page(entries(6, 1)))

    renderSidebar()

    await screen.findByText('Conversation 25')
    expect(titles()).toHaveLength(20)
    await userEvent.click(screen.getByRole('button', { name: 'Show more' }))

    await waitFor(() => expect(titles()).toHaveLength(25))
    expect(new Set(titles()).size).toBe(25)
    expect(listConversations).toHaveBeenLastCalledWith('cursor-1')
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('keeps the entries and Show more when the next page fails, and tries again', async () => {
    vi.mocked(listConversations)
      .mockResolvedValueOnce(page(entries(25, 6), 'cursor-1'))
      .mockResolvedValueOnce({ error: CONVERSATIONS_LOAD_FAILED_MESSAGE })
      .mockResolvedValueOnce(page(entries(5, 1)))

    renderSidebar()

    await screen.findByText('Conversation 25')
    await userEvent.click(screen.getByRole('button', { name: 'Show more' }))

    expect(await screen.findByText(MORE_FAILED_MESSAGE)).toBeInTheDocument()
    expect(titles()).toHaveLength(20)
    await userEvent.click(screen.getByRole('button', { name: 'Show more' }))
    await waitFor(() => expect(titles()).toHaveLength(25))
    expect(screen.queryByText(MORE_FAILED_MESSAGE)).not.toBeInTheDocument()
  })

  // ── Choosing ──────────────────────────────────────────────────────────────

  it('opens a conversation that is not the open one', async () => {
    vi.mocked(listConversations).mockResolvedValue(page([entry(2), entry(1)]))
    const { onOpen } = renderSidebar({ currentId: 'c2' })

    await userEvent.click(await screen.findByRole('button', { name: 'Conversation 1' }))

    expect(onOpen).toHaveBeenCalledWith('c1')
  })

  it('does nothing when the open conversation is chosen', async () => {
    vi.mocked(listConversations).mockResolvedValue(page([entry(2), entry(1)]))
    const { onOpen } = renderSidebar({ currentId: 'c2' })

    await userEvent.click(await screen.findByRole('button', { name: 'Conversation 2' }))

    expect(onOpen).not.toHaveBeenCalled()
  })

  it('always shows New chat, available only when the chat can start a new one', async () => {
    vi.mocked(listConversations).mockResolvedValue(page([entry(1)]))
    const { onNewChat, rerender } = renderSidebar({ canStartNewChat: false })

    await screen.findByText('Conversation 1')
    expect(screen.getByRole('button', { name: 'New chat' })).toBeDisabled()

    rerender({ canStartNewChat: true })
    await userEvent.click(screen.getByRole('button', { name: 'New chat' }))
    expect(onNewChat).toHaveBeenCalled()
  })

  it('makes the conversations and New chat unavailable while a reply streams', async () => {
    vi.mocked(listConversations).mockResolvedValue(page([entry(1)]))

    renderSidebar({ busy: true, canStartNewChat: true })

    expect(await screen.findByRole('button', { name: 'Conversation 1' })).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByRole('button', { name: 'New chat' })).toBeDisabled()
  })

  // ── Following activity ────────────────────────────────────────────────────

  it('reloads the first page when told to, moving a conversation to the top without repeating it', async () => {
    vi.mocked(listConversations)
      .mockResolvedValueOnce(page(entries(25, 6), 'cursor-1'))
      .mockResolvedValueOnce(page(entries(5, 1)))
      // Conversation 3 was continued, so it is now the most recent.
      .mockResolvedValueOnce(page([entry(3), ...entries(25, 7)], 'cursor-2'))

    const { rerender } = renderSidebar()
    await screen.findByText('Conversation 25')
    await userEvent.click(screen.getByRole('button', { name: 'Show more' }))
    await waitFor(() => expect(titles()).toHaveLength(25))

    rerender({ refreshKey: 1 })

    await waitFor(() => expect(titles()[0]).toBe('Conversation 3'))
    expect(titles()).toHaveLength(25)
    expect(titles().filter((t) => t === 'Conversation 3')).toHaveLength(1)
    // Everything is loaded already, so there is still nothing more to show.
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('keeps its entries and shows no error when a reload fails', async () => {
    vi.mocked(listConversations)
      .mockResolvedValueOnce(page([entry(2), entry(1)]))
      .mockResolvedValueOnce({ error: CONVERSATIONS_LOAD_FAILED_MESSAGE })

    const { rerender } = renderSidebar()
    await screen.findByText('Conversation 2')

    rerender({ refreshKey: 1 })

    await waitFor(() => expect(listConversations).toHaveBeenCalledTimes(2))
    expect(titles()).toEqual(['Conversation 2', 'Conversation 1'])
    expect(screen.queryByText(CONVERSATIONS_LOAD_FAILED_MESSAGE)).not.toBeInTheDocument()
  })

  it('sends an ended session to sign in when a reload finds it', async () => {
    vi.mocked(listConversations)
      .mockResolvedValueOnce(page([entry(1)]))
      .mockResolvedValueOnce({ error: SESSION_EXPIRED_MESSAGE })

    const { rerender, onSessionExpired } = renderSidebar()
    await screen.findByText('Conversation 1')

    rerender({ refreshKey: 1 })

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalled())
  })
})
