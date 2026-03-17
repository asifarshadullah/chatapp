# Scenario 01: Design Decision

## Analysis

### What is the invariant?

"Archived conversations cannot receive new messages."

This is a hard rule. It must be impossible to violate regardless of what the Application or
API layer does. That means it must live inside the domain — specifically inside
`Conversation.AddMessage()`, the only choke point for adding messages.

### New entity or modify existing?

`IsArchived` is a state flag on a conversation. The conversation itself is still the same
thing being tracked. It does not have separate identity, a separate lifecycle, or its own
independent rules. → Modify `Conversation`, do not create a new entity.

### Does Archive() itself go in Domain or Application?

`Archive()` mutates the conversation's state with no I/O. It is pure in-memory logic that
enforces: "once archived, cannot be unarchived." → Domain method.

The workflow of *loading the conversation, calling Archive(), and saving it* requires I/O.
→ Application method (`ArchiveConversationAsync`).

---

## Layer split

### Domain changes — Conversation.cs

```csharp
public bool IsArchived { get; private set; }

public void Archive()
{
    if (IsArchived) return;   // idempotent — no error if already archived
    IsArchived = true;
}

public void AddMessage(ChatMessage message)
{
    ArgumentNullException.ThrowIfNull(message);
    if (IsArchived)
        throw new InvalidOperationException("Cannot add messages to an archived conversation.");
    _messages.Add(message);
}
```

Why idempotent? Calling `Archive()` twice should not throw — it just has no effect. This
makes the Application layer simpler (no need to check before calling) and is consistent with
how most state transitions are designed.

### Application changes

Add to `IChatService`:
```csharp
Task ArchiveConversationAsync(Guid conversationId, CancellationToken ct = default);
```

Implement in `ChatService`:
```csharp
public async Task ArchiveConversationAsync(Guid conversationId, CancellationToken ct = default)
{
    var conversation = await _repository.GetConversationAsync(conversationId, ct)
        ?? throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");

    conversation.Archive();  // domain enforces the rule

    await _repository.UpdateConversationAsync(conversation, ct);
}
```

Add to `IChatRepository`:
```csharp
Task UpdateConversationAsync(Conversation conversation, CancellationToken ct = default);
```

### Infrastructure changes — MongoChatRepository.cs

`ConversationDocument` gets a new field:
```csharp
public bool IsArchived { get; set; }
```

`ToConversation` mapping must include `IsArchived`. Since the domain's reconstruction
constructor does not expose `IsArchived` as a constructor parameter, Infrastructure calls
`Archive()` conditionally when reconstructing:

```csharp
private static Conversation ToConversation(ConversationDocument doc)
{
    var conversation = new Conversation(doc.Id, doc.CreatedAt);
    if (doc.IsArchived)
        conversation.Archive();   // restore archived state
    foreach (var m in doc.Messages.OrderBy(m => m.Timestamp))
        conversation.AddMessage(m);  // wait — this would throw if archived!
    return conversation;
}
```

Problem: if the conversation is archived, calling `AddMessage` on it during reconstruction
will throw. The domain rule is too strict for the reconstruction path.

Resolution options:
1. Add an `IsArchived` parameter to the reconstruction constructor (cleanest)
2. Add a separate internal method `AddMessageUnchecked` for reconstruction only (leaky)
3. Reconstruct messages before archiving (order-dependent, fragile)

Best: option 1 — update the reconstruction constructor:

```csharp
// Reconstruction constructor — updated
public Conversation(Guid id, DateTime createdAt, bool isArchived = false)
{
    Id = id;
    CreatedAt = createdAt;
    IsArchived = isArchived;
}
```

Then Infrastructure:
```csharp
private static Conversation ToConversation(ConversationDocument doc)
{
    var conversation = new Conversation(doc.Id, doc.CreatedAt, doc.IsArchived);
    foreach (var m in doc.Messages.OrderBy(m => m.Timestamp))
        conversation.AddMessage(m);
    return conversation;
}
```

Wait — still a problem. `AddMessage` still checks `IsArchived`. Need another approach.

Actually: the archived conversation's messages are already in the document. We reconstruct
without going through `AddMessage`:

```csharp
public Conversation(Guid id, DateTime createdAt, bool isArchived, IEnumerable<ChatMessage> messages)
{
    Id = id;
    CreatedAt = createdAt;
    IsArchived = isArchived;
    _messages.AddRange(messages);  // bypass AddMessage to avoid the archived check
}
```

This is a reconstruction constructor — it is trusted data. The messages existed before
archiving, so bypassing the check is correct.

This pattern reveals an important lesson: **domain invariants sometimes conflict with the
reconstruction path.** The solution is always a dedicated reconstruction constructor that
bypasses new-creation logic.

### API changes

Add endpoint to `ChatController`:
```csharp
[HttpDelete("{conversationId}")]
public async Task<IActionResult> ArchiveConversation(Guid conversationId, CancellationToken ct)
{
    try
    {
        await _chatService.ArchiveConversationAsync(conversationId, ct);
        return NoContent();
    }
    catch (KeyNotFoundException ex)
    {
        return NotFound(ex.Message);
    }
}
```

---

## What the implementation revealed

The biggest non-obvious problem: **reconstructing an archived conversation with existing
messages required a specialized constructor.** This is not obvious from the requirement alone.
It surfaces during implementation.

The learning: write the domain first, then write the infrastructure mapping, and the friction
between them shows you where the domain needs to be more expressive (e.g. the reconstruction
constructor).

Always ask: "Can I reconstruct this entity from storage without violating its own rules?"
If no, the entity needs a reconstruction path that bypasses new-creation guards.
