# The Domain Layer

## What it is

The domain layer captures what the problem IS, expressed in code, with no reference to any
technology. It models the real-world concepts and rules of the business.

The domain should be readable by a non-technical stakeholder who understands the business.
If you showed a product manager `Conversation`, `ChatMessage`, and `MessageRole`, they would
understand what the system is about. That is the goal.

---

## What belongs here

### Entities

Things that have identity — you can distinguish one from another and track them over time.

```csharp
public class Conversation
{
    public Guid Id { get; }          // identity
    public DateTime CreatedAt { get; }
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    public void AddMessage(ChatMessage message) { ... }  // behaviour
}
```

Key properties of an entity:
- Has a unique identity (usually a Guid or domain-specific ID)
- Owns its internal state (private backing fields, read-only exposure)
- Enforces its own invariants (rules that must always be true about it)
- Behaviour lives as methods, not as external logic operating on public fields

### Value Objects

Things defined by their values, not by identity. Two value objects with the same values are
identical. They are always immutable.

Example: a monetary amount. $10.00 USD is $10.00 USD — there is no meaningful difference
between two instances with the same amount and currency.

```csharp
public record Money(decimal Amount, string Currency);
// Two Money("10.00", "USD") instances are equal. They have no separate identity.
```

`ChatMessage` in ChatApp is close to an entity (it has a Guid), but its fields — Content,
Role, Timestamp — behave like value object fields: immutable once set.

### Enums representing domain concepts

```csharp
public enum MessageRole { User, Assistant }
```

This is not a database column value. It is the domain's vocabulary for "who sent this message."
The fact that it gets stored as a string in MongoDB is Infrastructure's problem, not Domain's.

### Business rules as invariants

An invariant is something that must always be true. Invariants live in domain methods and
constructors — the places that cannot be bypassed.

```csharp
// In ChatMessage constructor:
if (string.IsNullOrWhiteSpace(content))
    throw new ArgumentException("Content cannot be null, empty, or whitespace.");

if (content.Length > MaxContentLength)
    throw new ArgumentException($"Content cannot exceed {MaxContentLength} characters.");
```

There is no way to create a `ChatMessage` with empty content. The rule is enforced at the
only possible entry point. Application layer does not repeat this check. API layer does not
repeat this check. It is impossible to bypass.

### Domain events (when needed)

Something significant happened in the domain — other parts of the system may want to know.

```csharp
public class ConversationArchivedEvent
{
    public Guid ConversationId { get; }
    public DateTime ArchivedAt { get; }
}
```

The domain raises the event. Infrastructure or Application decides what to do with it
(send an email, update a read model, push a SignalR notification).

---

## What does NOT belong here

| Thing | Why it doesn't belong |
|---|---|
| `[BsonId]`, `[Column]`, `[JsonProperty]` | Persistence/serialization attributes — Infrastructure |
| `IMongoCollection`, `DbSet` | Technology references — Infrastructure |
| `Task<T>`, async/await | Domain reasons synchronously — it doesn't wait for I/O |
| DTOs, view models | Shaped for callers — Application |
| `HttpClient`, file streams | External I/O — Infrastructure |
| Framework base classes | Framework coupling — avoid entirely |

The smell: if you write `using MongoDB.*` or `using Microsoft.EntityFrameworkCore.*` in a
domain file, something is wrong.

---

## The two-constructor pattern

Every entity needs two constructors:

```csharp
// 1. Create new — generates identity, enforces ALL business rules
public ChatMessage(string content, MessageRole role)
{
    if (string.IsNullOrWhiteSpace(content)) throw ...;
    if (content.Length > MaxContentLength) throw ...;

    Id = Guid.NewGuid();       // generated here
    Content = content;
    Role = role;
    Timestamp = DateTime.UtcNow;  // captured here
}

// 2. Reconstruct from storage — trusts data, no new-creation logic
public ChatMessage(Guid id, string content, MessageRole role, DateTime timestamp)
{
    Id = id;
    Content = content;
    Role = role;
    Timestamp = timestamp;
}
```

Why: when MongoDB gives you back stored data, you are not *creating* a message — you are
*reconstructing* one that already passed all its rules when it was first created. The
reconstruction constructor trusts the data.

---

## Modify existing entity vs. create a new entity

Ask: *"Does this new concept have its own identity, lifecycle, and rules — independent of
the existing entity?"*

| Scenario | Answer | Decision |
|---|---|---|
| Add `IsArchived` to Conversation | No — it is a state flag on the same thing | Modify Conversation |
| Add message reactions | Yes — a reaction has its own identity (who, when, which emoji), its own rules, its own lifecycle | New Reaction entity |
| Add conversation title | No — just a property | Modify Conversation |
| Add subscription/billing | Yes — entirely separate lifecycle, separate rules, possibly separate team | New bounded context |

**Danger signal:** if you are adding 3+ fields and several methods all about the same new
concept onto an existing entity, it probably wants to be its own entity.

---

## Practical example: adding archive to Conversation

Feature: *"A conversation can be archived. Archived conversations cannot receive new messages."*

Step 1 — what is the invariant?
"Archived conversations cannot receive new messages" is a hard rule. It must be impossible
to violate. That forces it into `AddMessage()` — the only choke point.

Step 2 — implement in domain:

```csharp
public class Conversation
{
    public bool IsArchived { get; private set; }

    public void Archive()
    {
        if (IsArchived) return;  // idempotent — archiving an archived conversation is fine
        IsArchived = true;
    }

    public void AddMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (IsArchived)
            throw new InvalidOperationException("Cannot add messages to an archived conversation.");
        _messages.Add(message);
    }
}
```

Step 3 — Application orchestrates, Infrastructure persists. Domain is done.

---

## How to keep the domain in sync with features

The domain only changes when **business rules** change — not when technology changes.

Business rules change slowly and intentionally. If the product manager says "users can now
send messages up to 10,000 characters", that is a domain change (update `MaxContentLength`).
If the team decides to switch from MongoDB to PostgreSQL, the domain has zero changes.

Ask before changing the domain: *"Is this a new business rule, or a technology decision?"*
- Business rule → domain changes
- Technology decision → infrastructure changes
- New workflow → application changes
