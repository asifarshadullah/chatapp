import { Box } from '@mui/material'
import type { ChatMessage } from '../types/chat'

interface Props {
  message: ChatMessage
}

export function MessageBubble({ message }: Props) {
  return (
    <Box
      className={`bubble--${message.role}`}
      sx={{
        maxWidth: '70%',
        px: 1.5,
        py: 0.75,
        borderRadius: '1rem',
        lineHeight: 1.5,
        wordBreak: 'break-word',
        fontSize: '0.95rem',
        ...(message.role === 'user'
          ? {
              alignSelf: 'flex-end',
              bgcolor: '#1a1a1a',
              color: '#ffffff',
              borderBottomRightRadius: '0.25rem',
            }
          : {
              alignSelf: 'flex-start',
              bgcolor: '#f5f5f5',
              color: '#1a1a1a',
              borderBottomLeftRadius: '0.25rem',
            }),
      }}
    >
      {message.message}
    </Box>
  )
}
