import type { ChatMessage, ConversationHistory } from '../types/chat'
import { authorizedFetch } from './authorizedFetch'
import { SessionExpiredError, SESSION_EXPIRED_MESSAGE, CONNECT_FAILED_MESSAGE } from './sessionErrors'

const API_BASE = '/api'

export const SEND_FAILED_MESSAGE = 'Your message could not be sent.'
export const REPLY_INTERRUPTED_MESSAGE = 'The reply was interrupted.'
export const CONVERSATION_NOT_FOUND_MESSAGE = 'Conversation not found.'
export const HISTORY_LOAD_FAILED_MESSAGE = 'Your conversation could not be loaded.'

export interface StreamCallbacks {
  onConversationId: (id: string) => void
  onWord: (word: string) => void
  onComplete: () => void
  onError: (error: string) => void
}

export type HistoryResult = { messages: ChatMessage[] } | { error: string }

export async function getHistory(conversationId: string): Promise<HistoryResult> {
  try {
    const response = await authorizedFetch(`${API_BASE}/chat/${conversationId}/history`)
    if (response.status === 404) return { error: CONVERSATION_NOT_FOUND_MESSAGE }
    if (!response.ok) return { error: HISTORY_LOAD_FAILED_MESSAGE }

    const history: ConversationHistory = await response.json()
    return {
      messages: history.messages.map((record) => ({
        id: record.id,
        message: record.content,
        role: record.role === 'assistant' ? 'assistant' : 'user',
        timestamp: record.timestamp,
      })),
    }
  } catch (err) {
    return { error: err instanceof SessionExpiredError ? SESSION_EXPIRED_MESSAGE : HISTORY_LOAD_FAILED_MESSAGE }
  }
}

/** Every outcome ends in one onComplete or onError, except an abort, which reports nothing. */
export async function streamMessage(
  message: string,
  conversationId: string | undefined,
  callbacks: StreamCallbacks,
  signal?: AbortSignal,
): Promise<void> {
  let response: Response
  try {
    response = await authorizedFetch(`${API_BASE}/chat/messages`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message, conversationId: conversationId ?? null }),
      signal,
    })
  } catch (err) {
    if (signal?.aborted) return
    callbacks.onError(err instanceof SessionExpiredError ? SESSION_EXPIRED_MESSAGE : CONNECT_FAILED_MESSAGE)
    return
  }

  if (!response.ok || !response.body) {
    callbacks.onError((await problemDetail(response)) ?? SEND_FAILED_MESSAGE)
    return
  }

  try {
    for await (const event of readEvents(response.body)) {
      switch (event.name) {
        case 'conversation':
          callbacks.onConversationId(JSON.parse(event.data))
          break
        case 'token':
          callbacks.onWord(JSON.parse(event.data))
          break
        case 'done':
          callbacks.onComplete()
          return
        case 'error':
          callbacks.onError(JSON.parse(event.data).message ?? REPLY_INTERRUPTED_MESSAGE)
          return
      }
    }
  } catch {
    // reported below as an interrupted reply
  }
  if (signal?.aborted) return
  callbacks.onError(REPLY_INTERRUPTED_MESSAGE)
}

async function problemDetail(response: Response): Promise<string | null> {
  try {
    const body = await response.json()
    return typeof body?.detail === 'string' ? body.detail : null
  } catch {
    return null
  }
}

interface ServerSentEvent {
  name: string
  data: string
}

async function* readEvents(body: ReadableStream<Uint8Array>): AsyncGenerator<ServerSentEvent> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) return
      buffer += decoder.decode(value, { stream: true })

      let boundary: RegExpExecArray | null
      while ((boundary = /\r?\n\r?\n/.exec(buffer)) !== null) {
        const block = buffer.slice(0, boundary.index)
        buffer = buffer.slice(boundary.index + boundary[0].length)
        const event = parseEvent(block)
        if (event) yield event
      }
    }
  } finally {
    reader.releaseLock()
  }
}

function parseEvent(block: string): ServerSentEvent | null {
  let name = 'message'
  const data: string[] = []
  for (const line of block.split(/\r?\n/)) {
    if (line.startsWith('event:')) name = line.slice(6).trim()
    else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''))
  }
  return data.length > 0 ? { name, data: data.join('\n') } : null
}
