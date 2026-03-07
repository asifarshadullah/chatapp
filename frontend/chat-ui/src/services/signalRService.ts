import * as signalR from '@microsoft/signalr'

export interface StreamCallbacks {
  onConversationId: (id: string) => void
  onWord: (word: string) => void
  onComplete: () => void
  onError: (error: string) => void
}

class SignalRService {
  private _connection: signalR.HubConnection | null = null

  private get connection(): signalR.HubConnection {
    if (!this._connection) {
      this._connection = new signalR.HubConnectionBuilder()
        .withUrl('/chatHub')
        .withAutomaticReconnect()
        .build()
    }
    return this._connection
  }

  async start(): Promise<void> {
    const state = this.connection.state
    if (
      state === signalR.HubConnectionState.Connecting ||
      state === signalR.HubConnectionState.Connected
    ) {
      return
    }
    if (state === signalR.HubConnectionState.Disconnecting) {
      await new Promise<void>((resolve) => {
        const check = setInterval(() => {
          if (this.connection.state !== signalR.HubConnectionState.Disconnecting) {
            clearInterval(check)
            resolve()
          }
        }, 50)
      })
    }
    if (this.connection.state === signalR.HubConnectionState.Disconnected) {
      await this.connection.start()
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
