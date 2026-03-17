# The Application Layer

## What it is

The application layer captures what the system CAN DO — the use cases. It orchestrates domain
objects and infrastructure interfaces to fulfill a user's intent. It does not contain business
rules, and it does not contain technology details.

Think of it as a director: it calls domain objects (the actors) and repositories (the stagehands)
in the right sequence to accomplish a goal.

---

## What belongs here

### Use case / service methods

Each method is one use case: one thing a user can do.

```csharp
public class ChatService : IChatService
{
    public async Task<ChatResponseDto> SendMessageAsync(string content, Guid? conversationId, ...)
    {
        // 1. Use the domain to create a valid message
        var userMessage = new ChatMessage(content, MessageRole.User);

        // 2. Coordinate with infrastructure (via interface) to get or create a conversation
        var conversation = conversationId.HasValue
            ? await _repository.GetConversationAsync(conversationId.Value, ct)
              ?? throw new KeyNotFoundException(...)
            : await _repository.CreateConversationAsync(ct);

        // 3. Persist via infrastructure
        await _repository.AddMessageAsync(conversation.Id, userMessage, ct);

        // 4. Create the response (echo logic — application-level decision)
        var echoMessage = new ChatMessage($"Echo: {userMessage.Content}", MessageRole.Assistant);
        await _repository.AddMessageAsync(conversation.Id, echoMessage, ct);

        // 5. Return a DTO — not a domain entity
        return new ChatResponseDto(...);
    }
}
```

What this method does NOT do:
- Validate message content (that is domain — ChatMessage constructor does it)
- Know MongoDB exists (it talks to IChatRepository, not MongoChatRepository)
- Build an HTTP response (the controller does that from the DTO)

### Repository interfaces (owned by Application, implemented by Infrastructure)

```csharp
// In Chat.Application.Interfaces
public interface IChatRepository
{
    Task<Conversation> CreateConversationAsync(CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task AddMessageAsync(Guid conversationId, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default);
}
```

This is the most architecturally important decision: Application defines what it NEEDS from
storage, in domain terms. Infrastructure then fulfills that contract using whatever technology
it wants. Application never knows which implementation it gets — that is decided at startup
in Program.cs (the composition root).

### DTOs (Data Transfer Objects)

DTOs are shaped for what callers need to see. They are not domain entities.

```csharp
public record ChatResponseDto(Guid MessageId, string Content, string Role, DateTime Timestamp, Guid ConversationId);
public record ConversationHistoryDto(Guid ConversationId, List<ChatMessageDto> Messages);
```

Rules for DTOs:
- Immutable (use records or readonly properties)
- No behaviour — just data
- Shaped for the caller's needs, not for the domain's needs
- Can flatten, rename, or combine domain fields if that serves the caller better

Do not expose domain entities directly from Application methods. The caller (API layer) would
then depend on domain types, and any domain change would ripple outward. DTOs are the buffer.

### Service interfaces (IChatService)

```csharp
public interface IChatService
{
    Task<ChatResponseDto> SendMessageAsync(string content, Guid? conversationId, CancellationToken ct = default);
    Task<ConversationHistoryDto?> GetHistoryAsync(Guid conversationId, CancellationToken ct = default);
}
```

The API layer depends on `IChatService`, not on `ChatService`. This allows the API tests to
replace the service entirely if needed, and makes the dependency direction explicit.

---

## What does NOT belong here

| Thing | Why it doesn't belong |
|---|---|
| Business rules / invariants | Domain — Application delegates to domain objects for rules |
| MongoDB / SQL / file access | Infrastructure — Application uses interfaces only |
| HTTP concepts (status codes, headers) | API layer |
| Presentation formatting | API layer |
| Constructing domain events without raising them | Debatable, but generally domain raises, application handles |

---

## Application method vs. Domain method — the precise test

Ask: *"Does this logic enforce a rule that must be impossible to violate, using only in-memory
state — no I/O?"*

| If yes | Domain method |
|---|---|
| If no (requires I/O, coordinates multiple steps) | Application method |

The pattern in practice:

```
Application layer:
  load conversation from repo         ← I/O, must be application
  call conversation.Archive()         ← pure domain rule enforcement
  save conversation to repo           ← I/O, must be application
```

`Archive()` is domain. The load-call-save workflow around it is application.

---

## The orchestration pattern (step by step)

Every application service method follows this shape:

1. **Validate inputs** (only cross-cutting things like missing IDs — not business rules)
2. **Load domain objects** from repository
3. **Call domain methods** to enforce rules and mutate state
4. **Persist changes** via repository
5. **Return a DTO** — never a domain entity

```csharp
public async Task ArchiveConversationAsync(Guid conversationId, CancellationToken ct = default)
{
    // 1. Load
    var conversation = await _repository.GetConversationAsync(conversationId, ct)
        ?? throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");

    // 2. Domain enforces the rule
    conversation.Archive();

    // 3. Persist
    await _repository.UpdateConversationAsync(conversation, ct);

    // 4. No return needed (void/Task) — or return a status DTO if the caller needs it
}
```

---

## CancellationToken on every async method

Every public async method in Application accepts a `CancellationToken`. This is a project
standard (from CLAUDE.md), but also good practice: it allows the runtime to cancel long-running
operations when a client disconnects or a timeout occurs.

```csharp
Task<ChatResponseDto> SendMessageAsync(string content, Guid? conversationId, CancellationToken ct = default);
```

The `= default` makes it optional for callers, but it threads through to all I/O operations.

---

## Practical example: adding GetHistory

```csharp
public async Task<ConversationHistoryDto?> GetHistoryAsync(Guid conversationId, CancellationToken ct = default)
{
    // Load — returns null if not found (Application decides: null means "not found", not an exception)
    var conversation = await _repository.GetConversationAsync(conversationId, ct);
    if (conversation is null)
        return null;

    // Map domain entities → DTOs (no business logic, just projection)
    var messages = conversation.Messages
        .Select(m => new ChatMessageDto(m.Id, m.Content, m.Role.ToString().ToLowerInvariant(), m.Timestamp))
        .ToList();

    return new ConversationHistoryDto(conversationId, messages);
}
```

No business rules here. The decision to return `null` instead of throwing is an application
concern (how this use case handles missing conversations). The mapping from domain to DTO is
application concern. The domain entity (`Conversation`, `ChatMessage`) is never returned raw.
