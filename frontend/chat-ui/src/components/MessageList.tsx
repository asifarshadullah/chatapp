import { useEffect, useRef } from 'react'
import { Box } from '@mui/material'
import type { ChatMessage } from '../types/chat'
import { MessageBubble } from './MessageBubble'

interface Props {
  messages: ChatMessage[]
}

export function MessageList({ messages }: Props) {
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  return (
    <Box
      sx={{
        flex: 1,
        overflowY: 'auto',
        px: 2,
        py: 2,
        display: 'flex',
        flexDirection: 'column',
        gap: 1,
        maxWidth: 800,
        width: '100%',
        mx: 'auto',
      }}
    >
      {messages.map((msg) => (
        <MessageBubble key={msg.id} message={msg} />
      ))}
      <div ref={bottomRef} />
    </Box>
  )
}
