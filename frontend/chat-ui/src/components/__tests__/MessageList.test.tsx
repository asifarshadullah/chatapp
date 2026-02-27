import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MessageList } from '../MessageList'
import type { ChatMessage } from '../../types/chat'

const messages: ChatMessage[] = [
  { id: '1', message: 'Hello', role: 'user', timestamp: '' },
  { id: '2', message: 'Echo: Hello', role: 'assistant', timestamp: '' },
]

describe('MessageList', () => {
  // Cycle 1 — empty state
  it('renders empty container when no messages', () => {
    const { container } = render(<MessageList messages={[]} />)
    expect(container.firstChild).toBeInTheDocument()
    expect(screen.queryByText('Hello')).not.toBeInTheDocument()
  })

  // Cycle 2 — renders messages in order
  it('renders all messages in order', () => {
    render(<MessageList messages={messages} />)
    const items = screen.getAllByText(/Hello|Echo: Hello/)
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Hello')
    expect(items[1]).toHaveTextContent('Echo: Hello')
  })

  // Cycle 3 — scrolls to bottom
  it('scrolls to bottom when new messages are added', () => {
    const scrollIntoViewMock = vi.fn()
    window.HTMLElement.prototype.scrollIntoView = scrollIntoViewMock

    render(<MessageList messages={messages} />)

    expect(scrollIntoViewMock).toHaveBeenCalled()
  })
})
