import type { ModelsOffered } from './chatApi'

const MODEL_KEY = 'chatapp.model'

// Storage can be unavailable (private windows, blocked site data); the default then applies.
export function savedModel(): string | null {
  try {
    return localStorage.getItem(MODEL_KEY)
  } catch {
    return null
  }
}

export function saveModel(id: string) {
  try {
    localStorage.setItem(MODEL_KEY, id)
  } catch {
    // The choice then lasts only until the page is reloaded.
  }
}

/** The saved choice if this deployment offers it, otherwise the default, dropping the stale choice. */
export function modelInUse(offered: ModelsOffered): string {
  const saved = savedModel()
  if (saved !== null && offered.models.some((m) => m.id === saved)) return saved
  if (saved !== null) {
    try {
      localStorage.removeItem(MODEL_KEY)
    } catch {
      // Nothing to drop if storage is unavailable.
    }
  }
  return offered.defaultModel
}
