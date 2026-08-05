import * as signalR from '@microsoft/signalr'
import { authService } from './authService'

export interface StreamCallbacks {
  onConversationId: (id: string) => void
  onWord: (word: string) => void
  onComplete: () => void
  onError: (error: string) => void
}

class SignalRService {
  private _connection: signalR.HubConnection | null = null
  private _startPromise: Promise<void> | null = null

  private get connection(): signalR.HubConnection {
    if (!this._connection) {
      this._connection = new signalR.HubConnectionBuilder()
        .withUrl('/chatHub', {
          accessTokenFactory: () => authService.getToken() ?? '',
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
   */
  async start(): Promise<void> {
    if (this.connection.state === signalR.HubConnectionState.Connected) return
    if (this._startPromise) return this._startPromise

    this._startPromise = (async () => {
      while (this.connection.state === signalR.HubConnectionState.Disconnecting) {
        await new Promise((resolve) => setTimeout(resolve, 50))
      }
      if (this.connection.state === signalR.HubConnectionState.Disconnected) {
        await this.connection.start()
      }
    })()

    try {
      await this._startPromise
    } finally {
      this._startPromise = null
    }
  }

  async stop(): Promise<void> {
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
    } catch {
      callbacks.onError('Failed to connect to chat server.')
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
