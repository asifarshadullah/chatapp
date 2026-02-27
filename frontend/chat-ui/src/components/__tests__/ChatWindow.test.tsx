import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatWindow } from '../ChatWindow'
import * as chatApi from '../../services/chatApi'

const mockResponse = { id: '99', message: 'Echo: Hello', role: 'assistant', timestamp: '' }

describe('ChatWindow', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  // Cycle 1 — renders ChatInput and MessageList
  it('renders input and message list', () => {
    render(<ChatWindow />)
    expect(screen.getByRole('textbox')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument()
  })

  // Cycle 2 — adds user message to list on send
  it('adds user message to the list immediately on send', async () => {
    vi.spyOn(chatApi, 'sendMessage').mockResolvedValue(mockResponse)
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    expect(screen.getByText('Hello')).toBeInTheDocument()
  })

  // Cycle 3 — calls API and adds assistant response
  it('adds assistant response after API call', async () => {
    vi.spyOn(chatApi, 'sendMessage').mockResolvedValue(mockResponse)
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByText('Echo: Hello')).toBeInTheDocument())
  })

  // Cycle 4 — shows loading state
  it('disables input while waiting for API response', async () => {
    vi.spyOn(chatApi, 'sendMessage').mockImplementation(
      () => new Promise((resolve) => setTimeout(() => resolve(mockResponse), 100)),
    )
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    expect(screen.getByRole('textbox')).toBeDisabled()
    await waitFor(() => expect(screen.getByRole('textbox')).not.toBeDisabled())
  })

  // Cycle 5 — shows error on API failure
  it('shows error message when API call fails', async () => {
    vi.spyOn(chatApi, 'sendMessage').mockRejectedValue(new Error('API error: 500'))
    render(<ChatWindow />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /send/i }))
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
  })
})
