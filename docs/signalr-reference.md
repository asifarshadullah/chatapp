# SignalR Reference

A practical reference for incorporating SignalR into ASP.NET Core + React projects.

---

## What SignalR is

SignalR provides a **persistent, bidirectional connection** between browser and server. It negotiates the best available transport automatically:

1. **WebSockets** — preferred, full duplex
2. **Server-Sent Events** — server→client only fallback
3. **Long Polling** — oldest fallback, works everywhere

You never manage transports directly. The key mental model: a **Hub** is a class whose methods the browser can call remotely, and the server can push data back to the browser at any time.

```
Browser                           Server Hub
──────                            ──────────
connection.invoke("DoThing")  →   public Task DoThing() { ... }
                              ←   Clients.Caller.SendAsync("Result", data)
connection.on("Result", fn)   ←
```

---

## The 5 steps to wire up SignalR

### Step 1 — Register on the server

```csharp
// Program.cs
builder.Services.AddSignalR();

app.MapHub<YourHub>("/yourHub");   // URL the client connects to
```

No extra NuGet needed in ASP.NET Core 8 — SignalR is included.

### Step 2 — Create a Hub class

```csharp
public class YourHub : Hub
{
    private readonly IYourService _service;

    public YourHub(IYourService service)   // constructor injection works normally
    {
        _service = service;
    }

    // Method the client can call (request/response style):
    public async Task DoSomething(string input)
    {
        var result = await _service.ProcessAsync(input);
        await Clients.Caller.SendAsync("ReceiveResult", result);
    }

    // Server-side streaming — yields chunks one at a time:
    public async IAsyncEnumerable<string> StreamSomething(
        string input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in input.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Delay(50, cancellationToken);
        }
    }

    // Lifecycle hooks:
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
```

### Step 3 — Configure CORS (required for browser clients)

SignalR **requires** `AllowCredentials()` — without it the WebSocket handshake is rejected by the browser.

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("SignalRPolicy", policy =>
        policy
            .WithOrigins("http://localhost:5173")   // your frontend origin
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());                   // REQUIRED for SignalR
});

app.UseCors("SignalRPolicy");                       // must come before app.MapHub
```

### Step 4 — Install and connect on the client

```bash
npm install @microsoft/signalr
```

```typescript
import * as signalR from '@microsoft/signalr'

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/yourHub')             // same path as MapHub
    .withAutomaticReconnect()        // reconnects automatically on drop
    .build()

await connection.start()            // opens the connection
```

### Step 5 — Send and receive

**Call a server method (client → server):**
```typescript
// Fire and wait for completion:
await connection.invoke('DoSomething', 'my input')

// If the server method returns a value:
const result = await connection.invoke<string>('GetValue', 42)
```

**Listen for server pushes (server → client):**
```typescript
// Register BEFORE or immediately after start()
connection.on('ReceiveResult', (data: string) => {
    console.log(data)
})

// Remove a specific handler:
connection.off('ReceiveResult', handler)
```

**Server-side streaming — chunks arrive one at a time:**
```typescript
const stream = connection.stream<string>('StreamSomething', 'hello world')

stream.subscribe({
    next: (chunk) => console.log('chunk:', chunk),
    complete: () => console.log('done'),
    error: (err) => console.error(err),
})
```

---

## The three communication patterns

| Pattern | Direction | Use when |
|---|---|---|
| `invoke` / `SendAsync` | Client → Server → Client reply | Request/response (like HTTP but persistent) |
| `Clients.All.SendAsync` | Server → all clients | Broadcasts: notifications, live scores, chat rooms |
| `IAsyncEnumerable<T>` | Server → one client, streaming | LLM token streaming, file progress, live logs |

---

## Groups — for rooms and channels

```csharp
// Add a client to a group:
await Groups.AddToGroupAsync(Context.ConnectionId, "room-42");

// Broadcast to that group only:
await Clients.Group("room-42").SendAsync("NewMessage", msg);

// Remove from group:
await Groups.RemoveFromGroupAsync(Context.ConnectionId, "room-42");
```

Groups are the standard way to handle chat rooms, game lobbies, or any multi-user channel concept.

---

## Clients targets — who to push to

| Target | Meaning |
|---|---|
| `Clients.Caller` | Only the client who invoked the current hub method |
| `Clients.All` | Every connected client |
| `Clients.Others` | Everyone except the caller |
| `Clients.Group("name")` | All clients in that group |
| `Clients.Client(connectionId)` | A specific connection by ID |

---

## Checklist for any new project

```
Server:
  [ ] builder.Services.AddSignalR()
  [ ] app.MapHub<YourHub>("/path")
  [ ] CORS policy includes AllowCredentials()
  [ ] app.UseCors() comes before app.MapHub()
  [ ] Hub class inherits Hub, uses constructor injection
  [ ] [EnumeratorCancellation] on CancellationToken in streaming methods

Client:
  [ ] npm install @microsoft/signalr
  [ ] HubConnectionBuilder().withUrl(...).withAutomaticReconnect().build()
  [ ] connection.on(...) registered before or immediately after start()
  [ ] await connection.start() with error handling
  [ ] connection.invoke(...) to call server methods
  [ ] connection.stream(...).subscribe(...) for streaming
  [ ] await connection.stop() on cleanup / component unmount
```

---

## Vite proxy setup (dev only)

When the frontend dev server (port 5173) proxies to the backend (port 5064), add both HTTP and WebSocket proxy entries:

```typescript
// vite.config.ts
server: {
    proxy: {
        '/api': { target: 'http://localhost:5064', changeOrigin: true },
        '/chatHub': {
            target: 'http://localhost:5064',
            changeOrigin: true,
            ws: true,    // enables WebSocket proxying
        },
    },
}
```

---

## React integration patterns

### Singleton service pattern

Create one connection for the app lifetime rather than per-component:

```typescript
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
        if (state === signalR.HubConnectionState.Connecting ||
            state === signalR.HubConnectionState.Connected) {
            return  // already in progress or connected
        }
        if (state === signalR.HubConnectionState.Disconnecting) {
            // wait for cleanup to finish before reconnecting
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
}

export const signalRService = new SignalRService()  // singleton
```

### React Strict Mode — double-mount issue

React Strict Mode mounts every component **twice** in development (mount → cleanup → remount). This causes the first `start()` to be interrupted by `stop()`, which rejects the promise and can incorrectly set an error state.

Fix: use a `cancelled` flag in `useEffect`:

```typescript
useEffect(() => {
    let cancelled = false
    signalRService
        .start()
        .then(() => { if (!cancelled) setError(null) })
        .catch(() => { if (!cancelled) setError('Failed to connect.') })
    return () => {
        cancelled = true       // first mount: ignore its start() result
        signalRService.stop()
    }
}, [])
```

The `cancelled` flag ensures the first mount's rejected promise does not set the error state. The second mount connects successfully and clears any error via `.then()`.

### Streaming with in-place message updates

When streaming words into a message bubble, use a ref to track the streaming message ID and accumulated content. Avoid closing over stale state:

```typescript
const streamingIdRef = useRef<string | null>(null)
const streamingContentRef = useRef('')

// On each word:
streamingContentRef.current += word
const content = streamingContentRef.current
setMessages((prev) => {
    const exists = prev.some((m) => m.id === streamingIdRef.current)
    if (exists) {
        return prev.map((m) =>
            m.id === streamingIdRef.current ? { ...m, message: content } : m
        )
    }
    return [...prev, { id: streamingIdRef.current!, message: content, role: 'assistant', timestamp: new Date().toISOString() }]
})
```

---

## Streaming: echo vs. LLM

The hub code structure is the same for both cases. The only difference is where tokens come from:

```csharp
// Echo (current) — full response exists before streaming starts:
var response = await _chatService.SendMessageAsync(message, conversationId, ct);
foreach (var word in response.Message.Split(' '))
{
    yield return word + " ";
    await Task.Delay(50, ct);    // artificial delay simulates streaming
}

// LLM (future) — tokens arrive as the model generates them:
await foreach (var token in _chatService.StreamMessageAsync(message, conversationId, ct))
{
    yield return token;          // no Task.Delay needed — natural generation delay
}
```

Everything else — the hub method signature, `ReceiveConversationId`, the frontend `stream.subscribe()`, the word-by-word UI updates — stays exactly the same.

---

## Testing SignalR hubs

Use `WebApplicationFactory` with `HttpTransportType.LongPolling` — WebSockets are not supported by the test server's in-process handler.

```csharp
private HubConnection CreateHubConnection()
{
    return new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, "chatHub"), options =>
        {
            options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;   // required for test server
        })
        .Build();
}
```

Collecting a streaming response in a test:

```csharp
var words = new List<string>();
await foreach (var word in connection.StreamAsync<string>("SendMessage", "Hello World", null))
{
    words.Add(word);
}
var result = string.Join("", words).Trim();
result.Should().Be("Echo: Hello World");
```

For events sent via `Clients.Caller.SendAsync` before the stream, use a `TaskCompletionSource`:

```csharp
var tcs = new TaskCompletionSource<Guid>();
connection.On<Guid>("ReceiveConversationId", id => tcs.SetResult(id));
await connection.StartAsync();

// start consuming the stream on a background task so ReceiveConversationId fires:
var streamTask = Task.Run(async () =>
{
    await foreach (var _ in connection.StreamAsync<string>("SendMessage", "Hello", null)) { }
});

var conversationId = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
```
