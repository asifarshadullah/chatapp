# Decision Heuristics — Where Does This Go?

This is the file to read first when you are stuck. Run through the questions in order.

---

## Question 1: Which layer?

Ask these in order. Stop at the first match.

```
Is it a business concept — something meaningful even without technology?
  (a thing that exists in the real world, with rules)
  → Domain (entity, value object, enum, invariant)

Is it a workflow — orchestrating steps to accomplish a user goal?
  → Application (service method)

Is it a contract that Application needs but shouldn't implement?
  → Application (interface) + Infrastructure (implementation)

Is it technology-specific — database, file, HTTP call, email, queue?
  → Infrastructure

Is it HTTP routing, request parsing, or response shaping?
  → API layer
```

---

## Question 2: Domain or Application method?

*"Does this logic enforce a rule that must be impossible to violate,
using only in-memory state with no I/O?"*

| Yes | Domain method |
|---|---|
| No (requires I/O, coordinates multiple steps, crosses entities) | Application method |

Examples:

```
conversation.Archive()
  → No I/O. Enforces the rule: once archived, stays archived.
  → Domain method.

ChatService.ArchiveConversationAsync()
  → Loads from DB, calls domain, saves to DB. Coordinates I/O.
  → Application method.

message.AddReaction(userId, emoji)
  → No I/O. Enforces: one reaction per user per message.
  → Domain method.

ChatService.ReactToMessageAsync()
  → Loads conversation, finds message, calls domain, saves.
  → Application method.
```

---

## Question 3: Modify existing entity or create a new one?

*"Does this new concept have its own identity, lifecycle, and rules,
independent of the existing entity?"*

| No — it is a property or state of an existing thing | Modify the existing entity |
|---|---|
| Yes — it has its own identity, evolves independently, has its own rules | New entity |

Concrete signals that you need a new entity:
- You are adding 3+ fields all about the same concept to an existing entity
- The new concept has its own creation date, deletion, or status independent of its parent
- The new concept is referred to by multiple other entities (shared reference)
- A non-technical stakeholder would describe it as a separate thing

Concrete signals to just modify the existing entity:
- It is a single boolean or enum representing a state transition
- It does not exist independently of its parent
- It has no identity beyond being part of the parent

Examples:

| Scenario | Decision | Reason |
|---|---|---|
| Add `IsArchived` to Conversation | Modify Conversation | A flag. Conversation IS the thing being archived. |
| Add message reactions | New Reaction entity | Has own identity (who, what, when), own rules, own lifecycle |
| Add conversation title | Modify Conversation | A simple property. No separate identity. |
| Add read receipts per user | New ReadReceipt entity | Per-user, per-message state. Own identity. |
| Add pinned messages | Modify Conversation (add PinnedMessageIds list + Pin/Unpin methods) | Pinning is a state of the conversation's view of its messages |
| Add billing/subscriptions | New bounded context | Entirely different domain. Different rules. Different team. |

---

## Question 4: Same bounded context or new one?

A bounded context is a boundary within which a domain model is consistent and coherent.

Stay in one bounded context when:
- All concepts refer to the same core thing
- The same words mean the same thing throughout
- The system is small and focused

Split into a new bounded context when:
- The same word means different things in two areas
  (e.g. "User" in billing means "subscriber", "User" in support means "ticket submitter")
- Two areas can evolve completely independently
- Different teams own different areas
- One area's rules should not affect the other

For ChatApp: `Conversation`, `ChatMessage`, `MessageRole` all belong in one context.
If you add billing, that is a new bounded context — it has its own User, its own entities,
its own rules that have nothing to do with conversations.

---

## Question 5: Interface in Application or Infrastructure?

Always Application. The rule:

*The layer that USES something defines the interface.
The layer that PROVIDES it provides the implementation.*

```
Application uses storage → IChatRepository in Application, MongoChatRepository in Infrastructure
Application uses AI → IAiProvider in Application, ClaudeAiProvider in Infrastructure
Application uses email → IEmailSender in Application, SendGridEmailSender in Infrastructure
```

If the interface were in Infrastructure, Application would have to reference Infrastructure
to use it — that violates the dependency rule.

---

## The dependency rule (the overriding principle)

```
Domain ← Application ← Infrastructure
                    ← API
```

Arrows mean "depends on". Domain depends on nothing. Everything else depends on something
closer to the center.

Violations to watch for:
- Domain importing Application types → wrong
- Domain importing Infrastructure types → wrong
- Application importing Infrastructure types directly → wrong (use interfaces)
- API containing business logic → wrong (should be in Application or Domain)

If adding a `using` statement creates a circular dependency or points the wrong direction,
you have the wrong layer.

---

## The replacement test

For any class you write, ask:
*"If I replaced MongoDB with PostgreSQL tomorrow, would this class change?"*

- Yes → Infrastructure (or you have Infrastructure details leaking into the wrong layer)
- No → Domain or Application (correct placement)

Then ask:
*"If I replaced the business rule (e.g. max message length changed), would this class change?"*

- Yes → should be Domain (or you have business rules leaking into the wrong layer)
- No → correct placement

---

## Common mistakes and how to catch them

**Business logic in the Application layer:**
```csharp
// WRONG — application service doing domain validation
public async Task SendMessageAsync(string content, ...)
{
    if (string.IsNullOrWhiteSpace(content)) throw ...;  // this is a domain rule
    if (content.Length > 5000) throw ...;               // this belongs in ChatMessage constructor
    ...
}
```

Fix: move the validation into the domain entity constructor.

**Domain entity with persistence attributes:**
```csharp
// WRONG — domain entity knowing about MongoDB
public class Conversation
{
    [BsonId]  // NO
    public Guid Id { get; set; }
}
```

Fix: create a separate `ConversationDocument` in Infrastructure. Map between them in the repository.

**Repository returning domain entities in Application, but mapping inside Application:**
```csharp
// WRONG — Application doing the mapping that belongs in Infrastructure
public async Task<ConversationHistoryDto> GetHistoryAsync(...)
{
    var doc = await _mongoCollection.Find(...).FirstOrDefaultAsync();  // accessing MongoDB directly
    var conversation = new Conversation(doc.Id, doc.CreatedAt);       // mapping in Application
    ...
}
```

Fix: Application should only call `IChatRepository`, never `IMongoCollection`.

**Application returning domain entities to the API:**
```csharp
// WRONG — domain entity leaking to the API layer
public async Task<Conversation> GetHistoryAsync(Guid id, ...)
{
    return await _repository.GetConversationAsync(id, ct);  // raw domain entity returned
}
```

Fix: map to a DTO inside the Application service before returning.

---

## Quick reference card

| I am writing... | It goes in... |
|---|---|
| An entity with identity and business rules | Domain |
| A value object (immutable, defined by value) | Domain |
| An enum representing a business concept | Domain |
| A business rule / invariant | Domain (in constructor or method) |
| A domain event | Domain |
| A use case workflow (load → act → save) | Application |
| A repository/service interface | Application |
| A DTO for the API to consume | Application |
| A service interface (IChatService) | Application |
| A MongoDB/SQL repository implementation | Infrastructure |
| A database document/schema model | Infrastructure |
| A settings class (MongoDbSettings) | Infrastructure |
| An external HTTP client (AI, email, SMS) | Infrastructure |
| A controller method | API |
| DI wiring / composition root | API (Program.cs) |
| Docker, CI config | Ops / Infrastructure (not a code layer) |
