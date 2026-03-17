# Iteration 7: Local LLM Integration (Ollama)

## Goal
Replace the echo response with real AI responses from a locally-running LLM via Ollama.
The chat streams tokens word-by-word in real time using the existing SignalR infrastructure.
Application and Domain layers remain completely unaware that Ollama exists.

## Context
Iteration 6 added SignalR streaming — the hub streams words from the echo response one at a time.
This iteration swaps that echo for a real LLM. The hub already knows how to stream;
we just replace the source of the words with Ollama tokens instead of echo words.

The key architectural move: define `IAiProvider` in Application, implement `OllamaAiProvider`
in Infrastructure. `ChatService` and `ChatHub` only see the interface. Swapping Ollama for
OpenAI, Azure OpenAI, or any other provider in the future requires zero changes outside Infrastructure.

## Prerequisites
- Iterations 1–6 complete — SignalR streaming working with echo responses
- Ollama installed and running on Windows (`winget install Ollama.Ollama`)
- At least one model pulled (`ollama pull gemma2:2b` or `phi3:mini` or `llama3.2:3b`)
- Verify Ollama API is up: `curl http://localhost:11434/api/tags`

---

## Architecture overview

```
ChatHub (API)
  └─ IChatService.StreamResponseAsync()       ← Application interface (new method)
       ├─ IChatRepository (existing)           ← save user msg + final assistant msg
       └─ IAiProvider.StreamCompletionAsync()  ← Application interface (new)
            └─ OllamaAiProvider               ← Infrastructure implementation (new)
                  └─ Ollama REST API           ← http://localhost:11434
```

**Data flow:**
1. Hub receives user message
2. ChatService saves user message to MongoDB
3. ChatService calls IAiProvider → tokens stream back
4. Hub forwards each token to the browser via SignalR
5. After stream completes, ChatService saves the full assistant message to MongoDB

---

## Phase 1: IAiProvider Interface (Application layer)

### Task 1.1: Define the interface
**File:** `backend/src/Chat.Application/Interfaces/IAiProvider.cs`

```csharp
namespace Chat.Application.Interfaces;

/// <summary>
/// Provides streaming AI completions given a conversation history.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Streams completion tokens for the given conversation history.
    /// Tokens are yielded as they arrive from the model.
    /// </summary>
    IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}
```

Note: `history` is the full conversation history including the user's latest message.
The provider decides how to format it for the model (system prompt, message roles, etc.).

---

## Phase 2: Update ChatService (TDD)

ChatService gets a new method: `StreamResponseAsync`. This handles the full use case —
create user message, stream AI tokens, save the complete assistant message at the end.

### Task 2.1: Add streaming method to IChatService
**File:** `backend/src/Chat.Application/Interfaces/IChatService.cs`

Add:
```csharp
/// <summary>
/// Saves the user message, streams AI response tokens, then saves the assistant message.
/// Yields (conversationId, token) pairs — conversationId is sent on the first yield only.
/// </summary>
IAsyncEnumerable<(Guid ConversationId, string Token)> StreamResponseAsync(
    string content,
    Guid? conversationId = null,
    CancellationToken ct = default);
```

The tuple approach sends `ConversationId` on the very first yield so the hub can
forward it to the client before streaming begins — matches the existing `ReceiveConversationId` pattern.

### ChatService TDD cycles

**File:** `backend/tests/Chat.Application.Tests/Services/ChatServiceStreamingTests.cs`

Use a `FakeAiProvider` test double:
```csharp
private class FakeAiProvider : IAiProvider
{
    public IReadOnlyList<ChatMessage>? ReceivedHistory { get; private set; }
    public IEnumerable<string> Tokens { get; set; } = ["Hello", " world"];

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ReceivedHistory = history;
        foreach (var token in Tokens)
        {
            yield return token;
            await Task.Yield();
        }
    }
}
```

- **Cycle 2.2:** RED `StreamResponseAsync_YieldsConversationIdOnFirstToken`
  → GREEN inject `IAiProvider`, create user message, call provider, yield `(conversationId, token)` pairs

- **Cycle 2.3:** RED `StreamResponseAsync_SavesUserMessageBeforeStreaming`
  → GREEN add message to repo before iterating provider

- **Cycle 2.4:** RED `StreamResponseAsync_SavesCompleteAssistantMessageAfterStream`
  → GREEN buffer full response, save assistant message after `await foreach` completes

- **Cycle 2.5:** RED `StreamResponseAsync_PassesFullHistoryToProvider`
  → GREEN verify `FakeAiProvider.ReceivedHistory` includes both prior messages + new user message

- **Cycle 2.6:** RED `StreamResponseAsync_WithNoConversationId_CreatesNewConversation`
  → GREEN (should pass from Cycle 2.2 impl — same pattern as `SendMessageAsync`)

- Refactor: extract `BufferAndSave` helper if needed

---

## Phase 3: Update ChatHub (TDD)

Replace the word-splitting echo with the new streaming method.

### Hub TDD cycles

**File:** `backend/tests/Chat.Api.Tests/Hubs/ChatHubTests.cs`

- **Cycle 3.1:** RED `SendMessage_StreamsLlmTokens`
  → GREEN update `ChatHub.SendMessage` to call `IChatService.StreamResponseAsync`,
     send `ReceiveConversationId` on first tuple, then yield tokens via `IAsyncEnumerable`

- **Cycle 3.2:** RED `SendMessage_StoresMessagesViaStreamingMethod`
  → GREEN verify messages are persisted (via InMemoryChatRepository in hub tests)

- **Cycle 3.3:** Existing hub tests — update mocks/fakes to satisfy new `IChatService` signature.
  All existing hub tests should pass after this update.

### Updated ChatHub sketch
```csharp
public async IAsyncEnumerable<string> SendMessage(
    string content,
    Guid? conversationId,
    [EnumeratorCancellation] CancellationToken ct)
{
    bool conversationIdSent = false;

    await foreach (var (convId, token) in _chatService.StreamResponseAsync(content, conversationId, ct))
    {
        if (!conversationIdSent)
        {
            await Clients.Caller.SendAsync("ReceiveConversationId", convId, ct);
            conversationIdSent = true;
        }
        yield return token;
    }
}
```

---

## Phase 4: OllamaAiProvider (Infrastructure)

### Task 4.1: Add OllamaSharp NuGet package
**File:** `backend/src/Chat.Infrastructure/Chat.Infrastructure.csproj`
```xml
<PackageReference Include="OllamaSharp" Version="4.*" />
```

OllamaSharp is the official Ollama .NET client. It handles the streaming HTTP protocol,
JSON deserialization, and the chat message format. This keeps `OllamaAiProvider` clean.

### Task 4.2: Create OllamaSettings
**File:** `backend/src/Chat.Infrastructure/Configuration/OllamaSettings.cs`
```csharp
namespace Chat.Infrastructure.Configuration;

public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma2:2b";
}
```

### Task 4.3: Add settings to appsettings
**File:** `backend/src/Chat.Api/appsettings.Development.json`
```json
{
  "MongoDB": { ... },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "gemma2:2b"
  }
}
```

Change `Model` to whichever model you pulled locally.

### Task 4.4: Implement OllamaAiProvider
**File:** `backend/src/Chat.Infrastructure/AI/OllamaAiProvider.cs`

```csharp
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace Chat.Infrastructure.AI;

/// <summary>
/// Streams AI completions from a locally-running Ollama instance.
/// </summary>
public class OllamaAiProvider : IAiProvider
{
    private readonly OllamaApiClient _client;
    private readonly string _model;

    public OllamaAiProvider(IOptions<OllamaSettings> settings)
    {
        _client = new OllamaApiClient(settings.Value.BaseUrl);
        _model = settings.Value.Model;
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = history.Select(m => new Message
        {
            Role = m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant,
            Content = m.Content
        }).ToList();

        var request = new ChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = true
        };

        await foreach (var response in _client.StreamChatAsync(request, ct))
        {
            var token = response?.Message?.Content;
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }
}
```

### Task 4.5: Register in Program.cs
**File:** `backend/src/Chat.Api/Program.cs`

```csharp
// Ollama AI provider
builder.Services.Configure<OllamaSettings>(
    builder.Configuration.GetSection("Ollama"));

builder.Services.AddScoped<IAiProvider, OllamaAiProvider>();
```

Also update ChatService DI registration to accept the new `IAiProvider` dependency:
```csharp
builder.Services.AddScoped<IChatService, ChatService>();
// ChatService constructor now takes both IChatRepository and IAiProvider
```

---

## Phase 5: Test isolation — FakeAiProvider for API tests

API integration tests (`Chat.Api.Tests`) must not require Ollama to be running.
Update `ChatApiFactory` to also replace `IAiProvider`:

**File:** `backend/tests/Chat.Api.Tests/Infrastructure/ChatApiFactory.cs`

```csharp
// Add inside ConfigureServices:
var aiDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiProvider));
if (aiDescriptor is not null) services.Remove(aiDescriptor);

services.AddSingleton<IAiProvider, FakeAiProvider>();
```

Create a shared `FakeAiProvider` under `Chat.Api.Tests/Fakes/`:
```csharp
public class FakeAiProvider : IAiProvider
{
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "Fake";
        yield return " response";
        await Task.Yield();
    }
}
```

---

## Phase 6: Verify everything works

### Task 6.1: Run all backend tests (no Ollama needed)
```bash
dotnet test backend/ChatApp.sln --verbosity normal
```
All tests should pass. `ChatApiFactory` substitutes both MongoDB and Ollama.

### Task 6.2: Manual end-to-end test

1. Ensure Ollama is running (it auto-starts on Windows after install)
2. Start MongoDB: `docker compose up -d`
3. Start backend: `cd backend/src/Chat.Api && dotnet run`
4. Start frontend: `cd frontend/chat-ui && npm run dev`
5. Open browser, type a message, send it
6. You should see the AI response stream in token by token

### Task 6.3: Verify conversation history works
After a response, send a follow-up message. The model should have context of the prior exchange
(because `StreamResponseAsync` passes the full history to `IAiProvider`).

---

## Acceptance criteria
1. Sending a message returns a real LLM response, not an echo
2. Response streams token by token via SignalR (same UX as before, different source)
3. Conversation history is passed to the model — follow-up questions work contextually
4. All existing tests pass without Ollama running (FakeAiProvider substituted in tests)
5. Switching the model requires only changing `appsettings.Development.json` — zero code changes
6. `IAiProvider` has zero Ollama-specific references — it speaks only in domain terms (`ChatMessage`)

## Verification commands
```bash
# Run all tests (Ollama not needed)
dotnet test backend/ChatApp.sln --verbosity normal

# Run backend
cd backend/src/Chat.Api && dotnet run

# Check which models are available locally
ollama list

# Pull a different model if needed
ollama pull phi3:mini
```

## What you will learn
- Interface abstraction in its purest form — Application defines what it needs, Infrastructure provides it
- `IAsyncEnumerable<T>` for true token streaming — how async streams compose through layers
- The `[EnumeratorCancellation]` attribute and why it matters for cooperative cancellation
- Test doubles for external services — FakeAiProvider keeps tests fast and offline
- Configuration pattern reused: OllamaSettings mirrors MongoDbSettings
- How to pass full conversation history to a model for contextual responses

## Decisions log entries (add to PROGRESS.md after completion)
| Decision | Reason |
|---|---|
| OllamaSharp NuGet over raw HttpClient | Handles NDJSON streaming protocol, avoids low-level HTTP parsing; Infrastructure detail |
| IAiProvider in Application, not Infrastructure | Application defines what it needs; Infrastructure fulfills it — dependency inversion |
| Tuple `(Guid ConversationId, string Token)` from StreamResponseAsync | ConversationId must reach the hub before first token; tuple avoids two separate calls |
| FakeAiProvider in ChatApiFactory | API tests stay offline; only manual/E2E tests require Ollama running |
| Full history passed to provider | Model has conversation context; stateless provider, stateful conversation |
