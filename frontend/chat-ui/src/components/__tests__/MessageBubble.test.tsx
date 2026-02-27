import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MessageBubble } from '../MessageBubble'
import type { ChatMessage } from '../../types/chat'

const userMessage: ChatMessage = { id: '1', message: 'Hello', role: 'user', timestamp: '2024-01-01T00:00:00Z' }
const assistantMessage: ChatMessage = { id: '2', message: 'Echo: Hello', role: 'assistant', timestamp: '2024-01-01T00:00:00Z' }

describe('MessageBubble', () => {
  // Cycle 1 — renders content
  it('renders message content', () => {
    render(<MessageBubble message={userMessage} />)
    expect(screen.getByText('Hello')).toBeInTheDocument()
  })

  // Cycle 2 — user styling
  it('applies user role class for user messages', () => {
    render(<MessageBubble message={userMessage} />)
    expect(screen.getByText('Hello')).toHaveClass('bubble--user')
  })

  // Cycle 3 — assistant styling
  it('applies assistant role class for assistant messages', () => {
    render(<MessageBubble message={assistantMessage} />)
    expect(screen.getByText('Echo: Hello')).toHaveClass('bubble--assistant')
  })
})
