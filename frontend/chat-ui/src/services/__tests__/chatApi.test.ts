import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  streamMessage,
  getHistory,
  getModels,
  MODELS_LOAD_FAILED_MESSAGE,
  SEND_FAILED_MESSAGE,
  REPLY_INTERRUPTED_MESSAGE,
  CONVERSATION_NOT_FOUND_MESSAGE,
  HISTORY_LOAD_FAILED_MESSAGE,
  type StreamCallbacks,
} from '../chatApi'
import { authService } from '../authService'
import {
  SessionExpiredError,
  SESSION_EXPIRED_MESSAGE,
  CONNECT_FAILED_MESSAGE,
} from '../sessionErrors'

const FUTURE = () => new Date(Date.now() + 60 * 60_000).toISOString()
const NEARLY_STALE = () => new Date(Date.now() + 30_000).toISOString()

function signedIn(expiry: string = FUTURE(), token = 'stored-token') {
  localStorage.setItem('auth_token', token)
  localStorage.setItem('auth_token_expiry', expiry)
  localStorage.setItem('auth_session', 'active')
}

function eventStream(...chunks: string[]): Response {
  const encoder = new TextEncoder()
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      chunks.forEach((c) => controller.enqueue(encoder.encode(c)))
      controller.close()
    },
  })
  return new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })
}

function problem(status: number, detail?: string): Response {
  return detail === undefined
    ? new Response(null, { status })
    : new Response(JSON.stringify({ status, detail }), {
        status,
        headers: { 'Content-Type': 'application/problem+json' },
      })
}

function recorder() {
  const calls: string[] = []
  const callbacks: StreamCallbacks = {
    onConversationId: (id) => calls.push(`id:${id}`),
    onWord: (word) => calls.push(`word:${word}`),
    onComplete: () => calls.push('complete'),
    onError: (error) => calls.push(`error:${error}`),
  }
  return { calls, callbacks }
}

const FULL_REPLY = [
  'event: conversation\ndata: "conv-1"\n\n',
  'event: token\ndata: "Hel"\n\n',
  'event: token\ndata: "lo"\n\n',
  'event: done\ndata: {}\n\n',
]

beforeEach(() => {
  localStorage.clear()
  vi.restoreAllMocks()
})

afterEach(() => vi.unstubAllGlobals())

describe('streamMessage', () => {
  it('delivers the conversation id, each token and completion in order', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(eventStream(...FULL_REPLY)))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual(['id:conv-1', 'word:Hel', 'word:lo', 'complete'])
  })

  it('reassembles events split across chunk boundaries', async () => {
    signedIn()
    const whole = FULL_REPLY.join('')
    const chunks = [whole.slice(0, 7), whole.slice(7, 40), whole.slice(40, 41), whole.slice(41)]
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(eventStream(...chunks)))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual(['id:conv-1', 'word:Hel', 'word:lo', 'complete'])
  })

  it('keeps a line break inside a token', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(eventStream(
      'event: conversation\ndata: "conv-1"\n\n',
      'event: token\ndata: "lo\\nworld"\n\n',
      'event: done\ndata: {}\n\n',
    )))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toContain('word:lo\nworld')
  })

  it('reports the message of an error event', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(eventStream(
      'event: conversation\ndata: "conv-1"\n\n',
      'event: token\ndata: "Hel"\n\n',
      'event: error\ndata: {"message":"The reply could not be completed."}\n\n',
    )))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual(['id:conv-1', 'word:Hel', 'error:The reply could not be completed.'])
  })

  it('reports an interruption when the stream ends without completing', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(eventStream(
      'event: conversation\ndata: "conv-1"\n\n',
      'event: token\ndata: "Hel"\n\n',
    )))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls.at(-1)).toBe(`error:${REPLY_INTERRUPTED_MESSAGE}`)
    expect(calls).not.toContain('complete')
  })

  it('reports the detail of a refusal', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      problem(403, 'Chat is not available on your current plan.'),
    ))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual(['error:Chat is not available on your current plan.'])
  })

  it('falls back to a general message for a refusal without a body', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(403)))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual([`error:${SEND_FAILED_MESSAGE}`])
  })

  it('reports an unreachable server as a connection failure', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual([`error:${CONNECT_FAILED_MESSAGE}`])
  })

  it('reports an ended session as such, not as a connection failure', async () => {
    vi.spyOn(authService, 'getValidToken').mockRejectedValue(new SessionExpiredError())
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks)

    expect(calls).toEqual([`error:${SESSION_EXPIRED_MESSAGE}`])
  })

  it('reports nothing once the caller has aborted', async () => {
    signedIn()
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => {
      controller.abort()
      throw new DOMException('The operation was aborted.', 'AbortError')
    }))
    const { calls, callbacks } = recorder()

    await streamMessage('Hello', undefined, callbacks, controller.signal)

    expect(calls).toEqual([])
  })

  it('posts the message with the token in the header, never in the URL', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue(eventStream(...FULL_REPLY))
    vi.stubGlobal('fetch', fetchMock)

    await streamMessage('Hello', 'conv-1', recorder().callbacks)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/chat/messages')
    expect(url).not.toContain('access_token')
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer stored-token')
    expect(JSON.parse(init.body as string)).toEqual({ message: 'Hello', conversationId: 'conv-1' })
  })

  it('waits for a renewal in progress and then sends with the renewed token', async () => {
    signedIn(NEARLY_STALE())
    let finishRenewal!: (response: Response) => void
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => new Promise<Response>((resolve) => { finishRenewal = resolve }))
      .mockResolvedValueOnce(eventStream(...FULL_REPLY))
    vi.stubGlobal('fetch', fetchMock)

    const sending = streamMessage('Hello', undefined, recorder().callbacks)
    await new Promise((r) => setTimeout(r, 10))

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][0]).toBe('/auth/refresh')

    finishRenewal(new Response(
      JSON.stringify({ accessToken: 'renewed-token', expiresAt: FUTURE(), userId: 'u' }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    ))
    await sending

    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(url).toBe('/api/chat/messages')
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer renewed-token')
  })
})

describe('getHistory', () => {
  it('returns the saved messages in order, in the shape the chat shows', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({
      conversationId: 'conv-1',
      messages: [
        { id: 'm1', content: 'Hello', role: 'user', timestamp: '2026-10-25T10:00:00Z' },
        { id: 'm2', content: 'Hi there', role: 'assistant', timestamp: '2026-10-25T10:00:01Z' },
      ],
    })))

    const result = await getHistory('conv-1')

    expect(result).toEqual({
      messages: [
        { id: 'm1', message: 'Hello', role: 'user', timestamp: '2026-10-25T10:00:00Z' },
        { id: 'm2', message: 'Hi there', role: 'assistant', timestamp: '2026-10-25T10:00:01Z' },
      ],
    })
  })

  it('reports a missing conversation as not found', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(404)))

    expect(await getHistory('conv-1')).toEqual({ error: CONVERSATION_NOT_FOUND_MESSAGE })
  })

  it('reports a server error as a failed load', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(500)))

    expect(await getHistory('conv-1')).toEqual({ error: HISTORY_LOAD_FAILED_MESSAGE })
  })

  it('reports an unreachable server as a failed load', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    expect(await getHistory('conv-1')).toEqual({ error: HISTORY_LOAD_FAILED_MESSAGE })
  })

  it('reports an ended session as such', async () => {
    vi.spyOn(authService, 'getValidToken').mockRejectedValue(new SessionExpiredError())

    expect(await getHistory('conv-1')).toEqual({ error: SESSION_EXPIRED_MESSAGE })
  })

  it('asks for the history with the token in the header', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ conversationId: 'conv-1', messages: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await getHistory('conv-1')

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/chat/conv-1/history')
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer stored-token')
  })
})

describe('getModels', () => {
  it('returns the offered options and the default', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue(Response.json({
      defaultModel: 'local',
      models: [{ id: 'local', name: 'Local · gemma3:4b' }, { id: 'groq', name: 'Groq · gpt-oss-120b' }],
    }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await getModels()

    expect(result).toEqual({
      defaultModel: 'local',
      models: [{ id: 'local', name: 'Local · gemma3:4b' }, { id: 'groq', name: 'Groq · gpt-oss-120b' }],
    })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/chat/models')
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer stored-token')
  })

  it('reports a failed response', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(500)))

    expect(await getModels()).toEqual({ error: MODELS_LOAD_FAILED_MESSAGE })
  })

  it('reports a network failure', async () => {
    signedIn()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    expect(await getModels()).toEqual({ error: MODELS_LOAD_FAILED_MESSAGE })
  })
})

describe('streamMessage and the model', () => {
  it('names the model when given one', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue(eventStream(...FULL_REPLY))
    vi.stubGlobal('fetch', fetchMock)

    await streamMessage('Hello', undefined, recorder().callbacks, undefined, 'groq')

    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ message: 'Hello', conversationId: null, model: 'groq' })
  })

  it('names no model when given none', async () => {
    signedIn()
    const fetchMock = vi.fn().mockResolvedValue(eventStream(...FULL_REPLY))
    vi.stubGlobal('fetch', fetchMock)

    await streamMessage('Hello', undefined, recorder().callbacks)

    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).not.toHaveProperty('model')
  })
})
