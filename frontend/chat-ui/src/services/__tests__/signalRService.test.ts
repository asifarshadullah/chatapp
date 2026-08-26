import { describe, it, expect, vi, beforeEach } from 'vitest'

/**
 * Regression tests for the connect lifecycle.
 *
 * These exist because of a bug that survived every other layer of testing: the
 * chat page reported "Failed to connect to chat server." on every load in dev,
 * while the hub itself was healthy and negotiate returned 200. React's
 * StrictMode mounts, unmounts and remounts each component, and the unmount
 * cleanup stopped the connection that the immediate remount was already
 * awaiting — so the remount was handed the rejection of an attempt that had
 * already been abandoned.
 */

type State = 'Disconnected' | 'Connecting' | 'Connected' | 'Disconnecting'

class FakeHubConnection {
  state: State = 'Disconnected'
  startCalls = 0
  private _pending: { resolve: () => void; reject: (e: Error) => void } | null = null

  start = vi.fn(() => {
    this.startCalls += 1
    this.state = 'Connecting'
    return new Promise<void>((resolve, reject) => {
      this._pending = { resolve, reject }
    })
  })

  stop = vi.fn(async () => {
    // Mirrors the real client: stopping mid-negotiation rejects the start.
    if (this._pending) {
      const pending = this._pending
      this._pending = null
      this.state = 'Disconnected'
      pending.reject(new Error('The connection was stopped during negotiation.'))
      return
    }
    this.state = 'Disconnected'
  })

  on = vi.fn()
  off = vi.fn()
  stream = vi.fn()

  /** Test hook: let the in-flight start() succeed. */
  settleConnected() {
    const pending = this._pending
    this._pending = null
    this.state = 'Connected'
    pending?.resolve()
  }

  /** Test hook: let the in-flight start() fail, as a transport error would. */
  settleFailed(message = 'transport failure') {
    const pending = this._pending
    this._pending = null
    this.state = 'Disconnected'
    pending?.reject(new Error(message))
  }
}

let fake: FakeHubConnection

vi.mock('@microsoft/signalr', () => {
  const builder = {
    withUrl: vi.fn(() => builder),
    withAutomaticReconnect: vi.fn(() => builder),
    build: vi.fn(() => fake),
  }
  return {
    // A plain function, not an arrow: the service calls `new HubConnectionBuilder()`.
    HubConnectionBuilder: function HubConnectionBuilder() { return builder },
    HubConnectionState: {
      Disconnected: 'Disconnected',
      Connecting: 'Connecting',
      Connected: 'Connected',
      Disconnecting: 'Disconnecting',
      Reconnecting: 'Reconnecting',
    },
  }
})

/** A fresh service instance per test — the module export is a singleton. */
async function loadService() {
  vi.resetModules()
  fake = new FakeHubConnection()
  return await import('../signalRService')
}

function signIn() {
  localStorage.setItem('auth_token', 'valid-token')
  localStorage.setItem('auth_token_expiry', new Date(Date.now() + 60_000).toISOString())
}

beforeEach(() => {
  localStorage.clear()
  signIn()
})

// ── The StrictMode regression ────────────────────────────────────────────────

describe('start after an aborted attempt', () => {
  it('survives the StrictMode mount/unmount/remount sequence', async () => {
    const { signalRService } = await loadService()

    // The exact dev-mode ordering, with no await between the steps — that is
    // what made the original bug reachable: the remount arrived while the
    // first attempt was still pending and was handed that dying promise.
    const first = signalRService.start() // mount 1
    first.catch(() => {})
    const stopping = signalRService.stop() // unmount cleanup
    const second = signalRService.start() // remount, immediately after

    expect(second).not.toBe(first)

    fake.settleConnected()
    await stopping.catch(() => {})
    await expect(second).resolves.toBeUndefined()
  })

  it('does not leave a failed attempt cached as the shared connect', async () => {
    const { signalRService } = await loadService()

    const first = signalRService.start()
    fake.settleFailed()
    await expect(first).rejects.toThrow()

    const second = signalRService.start()
    fake.settleConnected()
    await expect(second).resolves.toBeUndefined()
    expect(fake.startCalls).toBe(2)
  })
})

describe('concurrent start calls', () => {
  it('share a single connect attempt', async () => {
    const { signalRService } = await loadService()

    const a = signalRService.start()
    const b = signalRService.start()
    expect(fake.startCalls).toBe(1)

    fake.settleConnected()
    await Promise.all([a, b])
  })

  it('is a no-op once connected', async () => {
    const { signalRService } = await loadService()

    const first = signalRService.start()
    fake.settleConnected()
    await first

    await signalRService.start()
    expect(fake.startCalls).toBe(1)
  })
})

describe('stop', () => {
  it('waits for an in-flight connect instead of aborting it', async () => {
    const { signalRService } = await loadService()

    const starting = signalRService.start()
    const stopping = signalRService.stop()

    // stop() must not have killed the negotiation the caller is awaiting.
    fake.settleConnected()
    await expect(starting).resolves.toBeUndefined()
    await stopping
    expect(fake.stop).toHaveBeenCalled()
  })

  it('survives a connect that failed', async () => {
    const { signalRService } = await loadService()

    const starting = signalRService.start()
    starting.catch(() => {})
    fake.settleFailed()

    await expect(signalRService.stop()).resolves.toBeUndefined()
  })
})

// ── Expired session must not masquerade as an unreachable server ─────────────

describe('session expiry', () => {
  it('start rejects with SessionExpiredError when the token is gone', async () => {
    const { signalRService, SessionExpiredError } = await loadService()
    localStorage.clear()

    await expect(signalRService.start()).rejects.toBeInstanceOf(SessionExpiredError)
    expect(fake.startCalls).toBe(0)
  })

  it('start rejects with SessionExpiredError when the token has expired', async () => {
    const { signalRService, SessionExpiredError } = await loadService()
    localStorage.setItem('auth_token', 'stale')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() - 1000).toISOString())

    await expect(signalRService.start()).rejects.toBeInstanceOf(SessionExpiredError)
  })

  it('sendMessage reports an expired session distinctly from a dead server', async () => {
    const { signalRService, SESSION_EXPIRED_MESSAGE } = await loadService()
    localStorage.clear()
    const onError = vi.fn()

    await signalRService.sendMessage('hi', undefined, {
      onConversationId: vi.fn(), onWord: vi.fn(), onComplete: vi.fn(), onError,
    })

    expect(onError).toHaveBeenCalledWith(SESSION_EXPIRED_MESSAGE)
  })

  it('sendMessage still reports a genuine connect failure as such', async () => {
    const { signalRService, CONNECT_FAILED_MESSAGE } = await loadService()
    const onError = vi.fn()

    const sending = signalRService.sendMessage('hi', undefined, {
      onConversationId: vi.fn(), onWord: vi.fn(), onComplete: vi.fn(), onError,
    })
    fake.settleFailed()
    await sending

    expect(onError).toHaveBeenCalledWith(CONNECT_FAILED_MESSAGE)
  })
})
