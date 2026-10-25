import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { modelInUse, saveModel } from '../modelChoice'

const OFFERED = {
  defaultModel: 'local',
  models: [{ id: 'local', name: 'Local · gemma3:4b' }, { id: 'groq', name: 'Groq · gpt-oss-120b' }],
}

beforeEach(() => localStorage.clear())
afterEach(() => vi.restoreAllMocks())

describe('modelChoice', () => {
  it('uses the default when nothing is saved', () => {
    expect(modelInUse(OFFERED)).toBe('local')
  })

  it('uses a saved choice that is offered', () => {
    saveModel('groq')

    expect(modelInUse(OFFERED)).toBe('groq')
  })

  it('drops a saved choice that is not offered, in favour of the default', () => {
    saveModel('retired')

    expect(modelInUse(OFFERED)).toBe('local')
    expect(localStorage.getItem('chatapp.model')).toBeNull()
  })

  it('falls back to the default when storage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(modelInUse(OFFERED)).toBe('local')
  })
})
