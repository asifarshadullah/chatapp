import { useState } from 'react'
import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
} from '@mui/material'
import type { ModelsResult } from '../services/chatApi'

interface SettingsDialogProps {
  open: boolean
  /** Null while the list of offered models is loading. */
  models: ModelsResult | null
  model: string | null
  onSave: (model: string) => void
  onCancel: () => void
}

export function SettingsDialog({ open, models, model, onSave, onCancel }: SettingsDialogProps) {
  // The choice being edited; only Save hands it back.
  const [draft, setDraft] = useState<string | null>(null)
  const chosen = draft ?? model ?? ''

  function close(save: boolean) {
    // Saving the choice already in use would pin today's default against a later change of it.
    if (save && chosen && chosen !== model) onSave(chosen)
    else onCancel()
    setDraft(null)
  }

  return (
    <Dialog open={open} onClose={() => close(false)} aria-labelledby="settings-title" fullWidth maxWidth="xs">
      <DialogTitle id="settings-title">Settings</DialogTitle>
      <DialogContent>
        {models === null && <CircularProgress size={24} aria-label="loading models" />}
        {models !== null && 'error' in models && <Alert severity="error">{models.error}</Alert>}
        {models !== null && !('error' in models) && (
          <FormControl fullWidth sx={{ mt: 1 }}>
            <InputLabel id="settings-model-label">Model</InputLabel>
            <Select
              labelId="settings-model-label"
              label="Model"
              value={chosen}
              onChange={(e) => setDraft(e.target.value)}
            >
              {models.models.map((m) => (
                <MenuItem key={m.id} value={m.id}>
                  {m.name}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={() => close(false)}>Cancel</Button>
        <Button variant="contained" onClick={() => close(true)} disabled={!chosen || models === null || 'error' in models}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  )
}
