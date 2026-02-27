import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatInput } from '../ChatInput'

describe('ChatInput', () => {
  // Cycle 1 — renders textarea and send button
  it('renders textarea and send button', () => {
    render(<ChatInput onSend={vi.fn()} isLoading={false} />)
    expect(screen.getByRole('textbox')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument()
  })

  // Cycle 2 — calls onSend on button click
  it('calls onSend with message content when button clicked', async () => {
    const onSend = vi.fn()
    render(<ChatInput onSend={onSend} isLoading={false} />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    expect(onSend).toHaveBeenCalledWith('Hello')
  })

  // Cycle 3 — calls onSend on Enter (not Shift+Enter)
  it('calls onSend when Enter is pressed without Shift', async () => {
    const onSend = vi.fn()
    render(<ChatInput onSend={onSend} isLoading={false} />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello{Enter}')
    expect(onSend).toHaveBeenCalledWith('Hello')
  })

  it('does not call onSend when Shift+Enter is pressed', async () => {
    const onSend = vi.fn()
    render(<ChatInput onSend={onSend} isLoading={false} />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello{Shift>}{Enter}{/Shift}')
    expect(onSend).not.toHaveBeenCalled()
  })

  // Cycle 4 — clears input after sending
  it('clears input after sending', async () => {
    render(<ChatInput onSend={vi.fn()} isLoading={false} />)
    const input = screen.getByRole('textbox')
    await userEvent.type(input, 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    expect(input).toHaveValue('')
  })

  // Cycle 5 — disabled when empty
  it('disables send button when input is empty', () => {
    render(<ChatInput onSend={vi.fn()} isLoading={false} />)
    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled()
  })

  // Cycle 6 — disabled when loading
  it('disables input and button when isLoading is true', () => {
    render(<ChatInput onSend={vi.fn()} isLoading={true} />)
    expect(screen.getByRole('textbox')).toBeDisabled()
    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled()
  })
})
