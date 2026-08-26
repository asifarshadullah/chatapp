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
    this.settleFailedWith(new Error(message))
  }

  /** Test hook: fail the in-flight start() with a specific error, as a rejected token factory does. */
  settleFailedWith(error: Error) {
    const pending = this._pending
    this._pending = null
    this.state = 'Disconnected'
    pending?.reject(error)
  }
}

let fake: FakeHubConnection

/** The options the service handed to withUrl, so the token factory can be called. */
let withUrlOptions: { accessTokenFactory?: () => string | Promise<string> } | undefined
function builderOptions() { return withUrlOptions }

vi.mock('@microsoft/signalr', () => {
  const builder = {
    withUrl: vi.fn((_url: string, options?: typeof withUrlOptions) => {
      withUrlOptions = options
      return builder
    }),
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
  withUrlOptions = undefined
  fake = new FakeHubConnection()
  return await import('../signalRService')
}

/** Lets the renewal that start() performs before connecting settle. */
async function tick() {
  await new Promise((resolve) => setTimeout(resolve, 0))
  await new Promise((resolve) => setTimeout(resolve, 0))
}

function signIn() {
  localStorage.setItem('auth_token', 'valid-token')
  localStorage.setItem('auth_token_expiry', new Date(Date.now() + 60 * 60_000).toISOString())
  localStorage.setItem('auth_session', 'active')
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

    await tick()
    fake.settleConnected()
    await stopping.catch(() => {})
    await expect(second).resolves.toBeUndefined()
  })

  it('does not leave a failed attempt cached as the shared connect', async () => {
    const { signalRService } = await loadService()

    const first = signalRService.start()
    first.catch(() => {})
    await tick()
    fake.settleFailed()
    await expect(first).rejects.toThrow()

    const second = signalRService.start()
    await tick()
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
    await tick()
    expect(fake.startCalls).toBe(1)

    fake.settleConnected()
    await Promise.all([a, b])
  })

  it('is a no-op once connected', async () => {
    const { signalRService } = await loadService()

    const first = signalRService.start()
    await tick()
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
    await tick()
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
    await tick()
    fake.settleFailed()

    await expect(signalRService.stop()).resolves.toBeUndefined()
  })
})

// ── Expired session must not masquerade as an unreachable server ─────────────

describe('session expiry', () => {
  it('start no longer refuses outright when the stored token has lapsed', async () => {
    const { signalRService } = await loadService()
    localStorage.setItem('auth_token', 'stale')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() - 1000).toISOString())
    localStorage.setItem('auth_session', 'active')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        accessToken: 'renewed',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: 'u',
      }),
    }))

    // A lapsed token is renewed by the access token factory during negotiation, so the
    // connect must be attempted rather than refused before it starts.
    const starting = signalRService.start()
    await tick()
    expect(fake.startCalls).toBe(1)

    fake.settleConnected()
    await expect(starting).resolves.toBeUndefined()
  })

  it('start surfaces an ended session when renewal fails during connect', async () => {
    const { signalRService, SessionExpiredError } = await loadService()
    localStorage.clear()

    const starting = signalRService.start()
    starting.catch(() => {})
    await tick()
    fake.settleFailedWith(new SessionExpiredError())

    await expect(starting).rejects.toBeInstanceOf(SessionExpiredError)
  })

  it('sendMessage reports an expired session distinctly from a dead server', async () => {
    const { signalRService, SESSION_EXPIRED_MESSAGE, SessionExpiredError } = await loadService()
    localStorage.clear()
    const onError = vi.fn()

    const sending = signalRService.sendMessage('hi', undefined, {
      onConversationId: vi.fn(), onWord: vi.fn(), onComplete: vi.fn(), onError,
    })
    await tick()
    fake.settleFailedWith(new SessionExpiredError())
    await sending

    expect(onError).toHaveBeenCalledWith(SESSION_EXPIRED_MESSAGE)
  })

  it('sendMessage still reports a genuine connect failure as such', async () => {
    const { signalRService, CONNECT_FAILED_MESSAGE } = await loadService()
    const onError = vi.fn()

    const sending = signalRService.sendMessage('hi', undefined, {
      onConversationId: vi.fn(), onWord: vi.fn(), onComplete: vi.fn(), onError,
    })
    await tick()
    fake.settleFailed()
    await sending

    expect(onError).toHaveBeenCalledWith(CONNECT_FAILED_MESSAGE)
  })
})

// ── Renewal replaces outright refusal at connect time ────────────────────────

describe('access token factory', () => {
  it('hands the hub a renewed token when the stored one is stale', async () => {
    const { signalRService } = await loadService()
    localStorage.setItem('auth_token', 'stale-token')
    localStorage.setItem('auth_token_expiry', new Date(Date.now() + 1_000).toISOString())
    localStorage.setItem('auth_session', 'active')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        accessToken: 'renewed-token',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: 'user-1',
      }),
    }))

    const starting = signalRService.start()
    await tick()
    fake.settleConnected()
    await starting

    // A token that lapsed while the tab was idle is the ordinary case, not the end of
    // the session, so the connection must renew rather than refuse.
    const factory = builderOptions()?.accessTokenFactory
    await expect(factory?.()).resolves.toBe('renewed-token')
    vi.unstubAllGlobals()
  })

  it('reports an ended session when renewal is refused', async () => {
    const { signalRService, SessionExpiredError } = await loadService()
    localStorage.clear()

    // With no session there is nothing to renew against, so the connect must surface an
    // ended session rather than a transport failure.
    await expect(signalRService.start()).rejects.toBeInstanceOf(SessionExpiredError)
    expect(fake.startCalls).toBe(0)
  })
})
