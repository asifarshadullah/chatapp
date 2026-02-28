import { useState } from 'react'
import { Box, InputBase, IconButton, Paper } from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import MicIcon from '@mui/icons-material/Mic'
import GraphicEqIcon from '@mui/icons-material/GraphicEq'

interface Props {
  onSend: (message: string) => void
  isLoading: boolean
}

export function ChatInput({ onSend, isLoading }: Props) {
  const [value, setValue] = useState('')

  function handleSend() {
    const trimmed = value.trim()
    if (!trimmed) return
    onSend(trimmed)
    setValue('')
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement | HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  return (
    <Paper
      elevation={0}
      sx={{
        display: 'flex',
        alignItems: 'center',
        borderRadius: '50px',
        border: '1px solid #e0e0e0',
        px: 1,
        py: 0.5,
        width: '100%',
        maxWidth: 680,
        bgcolor: '#ffffff',
      }}
    >
      <IconButton size="small" sx={{ color: 'text.secondary', mr: 0.5 }}>
        <AddIcon fontSize="small" />
      </IconButton>

      <InputBase
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={handleKeyDown}
        disabled={isLoading}
        placeholder="Ask anything"
        sx={{ flex: 1, fontSize: '0.95rem' }}
      />

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
        <IconButton size="small" sx={{ color: 'text.secondary' }}>
          <MicIcon fontSize="small" />
        </IconButton>

        <IconButton
          onClick={handleSend}
          disabled={!value.trim() || isLoading}
          aria-label="Send"
          size="small"
          sx={{
            bgcolor: 'black',
            color: 'white',
            width: 36,
            height: 36,
            '&:hover': { bgcolor: '#333' },
            '&.Mui-disabled': { bgcolor: '#d0d0d0', color: '#888' },
          }}
        >
          <GraphicEqIcon fontSize="small" />
        </IconButton>
      </Box>
    </Paper>
  )
}
