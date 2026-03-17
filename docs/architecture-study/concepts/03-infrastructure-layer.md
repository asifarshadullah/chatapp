# The Infrastructure Layer

## What it is

Infrastructure is the layer where your application touches the outside world: databases,
file systems, external APIs, email providers, message queues. Everything here is pluggable —
in theory, you could replace any piece of it and the rest of the system would be unaffected.

The key characteristic: if you can imagine replacing it with something different (MongoDB →
PostgreSQL, SendGrid → Mailchimp, local file → S3), it belongs in Infrastructure.

---

## What belongs here

### Repository implementations

Application defines the interface. Infrastructure provides the implementation.

```
Chat.Application:    IChatRepository        (the contract — Application owns this)
Chat.Infrastructure: MongoChatRepository    (the MongoDB implementation)
Chat.Infrastructure: InMemoryChatRepository (the in-memory implementation for tests)
```

Both implementations satisfy the same interface. The rest of the system has no idea which one
is running — that decision is made in Program.cs (the composition root).

### Database document / schema models

```csharp
// In Chat.Infrastructure.Data.Documents — internal, never leaks out
internal class ConversationDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public List<ChatMessageDocument> Messages { get; set; } = new();
}
```

`ConversationDocument` is `internal` — deliberately. It is an implementation detail of the
MongoDB storage strategy. Nothing outside Infrastructure knows it exists. If you switch to
PostgreSQL, you delete this class and write Entity Framework models instead. The domain
entity `Conversation` does not change.

### Mapping functions (document ↔ domain entity)

```csharp
// Only place that speaks both "MongoDB document" and "domain entity" languages
private static Conversation ToConversation(ConversationDocument doc)
{
    var conversation = new Conversation(doc.Id, doc.CreatedAt);  // reconstruction constructor
    foreach (var m in doc.Messages.OrderBy(m => m.Timestamp))
        conversation.AddMessage(ToMessage(m));
    return conversation;
}

private static ChatMessage ToMessage(ChatMessageDocument doc) =>
    new(doc.Id, doc.Content, Enum.Parse<MessageRole>(doc.Role), doc.Timestamp);
```

This is the translation boundary. These are the only methods that know both sides.

### Configuration classes

```csharp
public class MongoDbSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}
```

Settings are bound from `appsettings.json` in Program.cs. The infrastructure class holds
the shape; the API layer binds the values at startup.

### External service clients

HTTP clients, email senders, SMS gateways, payment processors — all live here, behind
interfaces defined in Application.

```csharp
// Application defines the need:
public interface IAiProvider
{
    Task<string> GetCompletionAsync(string prompt, CancellationToken ct = default);
}

// Infrastructure fulfills it:
public class ClaudeAiProvider : IAiProvider
{
    public async Task<string> GetCompletionAsync(string prompt, CancellationToken ct = default)
    {
        // HTTP call to Anthropic API
    }
}
```

If you switch from Claude to OpenAI, you write `OpenAiProvider`, replace one line in Program.cs,
and the rest of the system is untouched.

---

## What does NOT belong here

| Thing | Why |
|---|---|
| Business rules | Domain |
| Use case orchestration | Application |
| Domain entities (Conversation, ChatMessage) | Domain — Infrastructure USES them, does not define them |
| Repository interfaces | Application — Infrastructure implements, not defines |
| HTTP routing | API layer |

---

## The pluggability test

For any class you are writing, ask: *"If I replaced the database / external service / technology
tomorrow, would this class change or be replaced?"*

- Yes → Infrastructure
- No → it belongs somewhere else (probably Domain or Application)

---

## DI lifetime decisions

This matters in Program.cs when registering services:

| What | Lifetime | Why |
|---|---|---|
| `IMongoClient` | Singleton | MongoDB driver manages its own connection pool. One instance for the entire app. |
| `IMongoDatabase` | Scoped | Per-request database handle. Fresh each request, shares the underlying pooled connection. |
| `IChatRepository` (MongoChatRepository) | Scoped | Should match IMongoDatabase lifetime. |
| `IChatService` | Scoped | Depends on IChatRepository which is Scoped. |

General rule: match the lifetime of your class to the shortest lifetime of its dependencies.
If your class depends on something Scoped, your class must be Scoped or Transient — never
Singleton (this causes "captive dependency" bugs).

---

## The two-constructor pattern — Infrastructure's role

When reading from the database, Infrastructure uses the "reconstruction" constructor — the
one that does not validate, does not generate a new ID, does not set a new timestamp:

```csharp
// Infrastructure calling the reconstruction constructor
private static ChatMessage ToMessage(ChatMessageDocument doc) =>
    new(doc.Id, doc.Content, Enum.Parse<MessageRole>(doc.Role), doc.Timestamp);
//   ^^^^^^^^^  — from storage, not generated fresh
```

The domain entity trusts this data because it was validated when it was first created.
Infrastructure is responsible for faithfully round-tripping data, not for re-validating it.

---

## Testing infrastructure: the isolation strategy

Infrastructure has two test approaches:

### 1. Real integration tests (against actual external system)

```csharp
// MongoDbFixture: creates a unique database per test run, drops it on dispose
public class MongoDbFixture : IDisposable
{
    public IMongoDatabase Database { get; }
    private readonly string _databaseName = $"chatapp_test_{Guid.NewGuid():N}";

    public MongoDbFixture()
    {
        var client = new MongoClient(ConnectionString);
        Database = client.GetDatabase(_databaseName);
    }

    public void Dispose() => _client.DropDatabase(_databaseName);
}
```

Each test gets a fresh, isolated database. Tests are independent. Docker must be running.
This tests the real behaviour: actual MongoDB queries, actual document schema, actual mapping.

### 2. Replace infrastructure in API tests

```csharp
// ChatApiFactory: swaps MongoChatRepository for InMemoryChatRepository in tests
public class ChatApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatRepository));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddSingleton<IChatRepository, InMemoryChatRepository>();
        });
    }
}
```

API integration tests do not need Docker. They test the full HTTP → Application → Domain
pipeline with a fast in-memory repository. This is only possible because Application depends
on the interface, not the concrete MongoDB implementation.

---

## Real ChatApp Infrastructure structure

```
Chat.Infrastructure/
  Configuration/
    MongoDbSettings.cs          ← POCO bound from appsettings
  Data/
    Documents/
      ConversationDocument.cs   ← MongoDB schema (internal)
  Repositories/
    MongoChatRepository.cs      ← implements IChatRepository using MongoDB
    InMemoryChatRepository.cs   ← implements IChatRepository using a dictionary (for tests)
```

Pattern to follow for every new external dependency:
1. Define the interface in Application
2. Create the implementation class in Infrastructure/[Technology]/
3. Create the document/schema model if needed (keep it internal)
4. Write mapping methods private to the implementation
5. Register in Program.cs
