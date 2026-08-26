import * as signalR from '@microsoft/signalr'
import { authService } from './authService'
import {
  SessionExpiredError,
  SESSION_EXPIRED_MESSAGE,
  CONNECT_FAILED_MESSAGE,
} from './sessionErrors'

export {
  SessionExpiredError,
  SESSION_EXPIRED_MESSAGE,
  CONNECT_FAILED_MESSAGE,
} from './sessionErrors'

export interface StreamCallbacks {
  onConversationId: (id: string) => void
  onWord: (word: string) => void
  onComplete: () => void
  onError: (error: string) => void
}

class SignalRService {
  private _connection: signalR.HubConnection | null = null
  private _startPromise: Promise<void> | null = null
  private _generation = 0

  private get connection(): signalR.HubConnection {
    if (!this._connection) {
      this._connection = new signalR.HubConnectionBuilder()
        .withUrl('/chatHub', {
          // Renews if the stored token is stale, and awaits a renewal already in
          // flight. Throwing beats sending '': an empty bearer token makes the hub
          // answer 401, which surfaces as an opaque transport failure.
          accessTokenFactory: () => authService.getValidToken(),
        })
        .withAutomaticReconnect()
        .build()
    }
    return this._connection
  }

  /**
   * Connects if needed. Concurrent callers share one attempt and all wait for it,
   * so a send issued while the connection is still negotiating does not proceed
   * against a connection that is not ready yet.
   *
   * The shared attempt is cleared inside the promise rather than around the
   * outer await: a caller that arrives after a failed attempt must get a fresh
   * connect, not the rejection of one that has already been abandoned.
   */
  async start(): Promise<void> {
    if (this.connection.state === signalR.HubConnectionState.Connected) return
    if (this._startPromise) return this._startPromise

    const generation = ++this._generation
    const attempt = (async () => {
      try {
        // Renew up front rather than relying on the token factory alone: SignalR wraps a
        // rejected factory in its own transport error, which would reach the UI as "server
        // unreachable" when the truth is that the session ended.
        await authService.getValidToken()

        while (this.connection.state === signalR.HubConnectionState.Disconnecting) {
          await new Promise((resolve) => setTimeout(resolve, 50))
        }
        if (this.connection.state === signalR.HubConnectionState.Disconnected) {
          await this.connection.start()
        }
      } finally {
        // Only the newest attempt may clear the shared slot, so a slow failure
        // cannot wipe out a newer connect that has since taken its place.
        if (this._generation === generation) this._startPromise = null
      }
    })()

    this._startPromise = attempt
    return attempt
  }

  /**
   * Tears the connection down for good — call on logout, not on component
   * unmount. Any in-flight connect is awaited first so stopping cannot abort a
   * negotiation that a still-mounted caller is waiting on.
   */
  async stop(): Promise<void> {
    await this._startPromise?.catch(() => {})
    await this.connection.stop()
  }

  async sendMessage(
    message: string,
    conversationId: string | undefined,
    callbacks: StreamCallbacks,
  ): Promise<void> {
    // The caller may send before the initial connection finished negotiating, or
    // after a reconnect dropped it. start() is a no-op when already connected.
    try {
      await this.start()
    } catch (err) {
      callbacks.onError(
        err instanceof SessionExpiredError ? SESSION_EXPIRED_MESSAGE : CONNECT_FAILED_MESSAGE,
      )
      return
    }

    const handler = (id: string) => {
      callbacks.onConversationId(id)
      this.connection.off('ReceiveConversationId', handler)
    }
    this.connection.on('ReceiveConversationId', handler)

    try {
      const stream = this.connection.stream<string>('SendMessage', message, conversationId ?? null)
      await new Promise<void>((resolve, reject) => {
        stream.subscribe({
          next: (word) => callbacks.onWord(word),
          error: (err: Error) => {
            callbacks.onError(err?.message ?? 'Connection error')
            reject(err)
          },
          complete: () => {
            callbacks.onComplete()
            resolve()
          },
        })
      })
    } catch {
      // Error already forwarded via callbacks.onError
    }
  }
}

export const signalRService = new SignalRService()
