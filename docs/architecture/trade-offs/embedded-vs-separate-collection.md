# Trade-off: Embedded Documents vs. Separate Collection (MongoDB)

## The choice

In MongoDB you have two ways to model a one-to-many relationship:

**Option A — Embedded (what ChatApp does)**
```json
{
  "_id": "conv-guid",
  "createdAt": "...",
  "isArchived": false,
  "messages": [
    { "id": "msg-guid-1", "content": "Hello", "role": "User", "timestamp": "..." },
    { "id": "msg-guid-2", "content": "Echo: Hello", "role": "Assistant", "timestamp": "..." }
  ]
}
```

**Option B — Separate collection**
```json
// conversations collection
{ "_id": "conv-guid", "createdAt": "...", "isArchived": false }

// messages collection
{ "_id": "msg-guid-1", "conversationId": "conv-guid", "content": "Hello", ... }
{ "_id": "msg-guid-2", "conversationId": "conv-guid", "content": "Echo: Hello", ... }
```

---

## When to embed (Option A)

Use embedding when:
- The child data is always accessed with the parent (you never need messages without the conversation)
- The child data is owned exclusively by the parent (a message belongs to exactly one conversation)
- The number of child items is bounded and reasonably small (not thousands per parent)
- You need atomic writes (MongoDB updates one document atomically — no transactions needed)
- Read performance matters most (one query returns everything)

ChatApp uses embedding correctly: you always fetch a conversation with its messages, messages
are exclusive to one conversation, and for a chat app, conversation length is bounded.

## When to use a separate collection (Option B)

Use a separate collection when:
- Child documents can grow unboundedly (a conversation could have millions of messages)
- Child documents are queried independently (e.g. "find all messages containing 'error'")
- Child documents are shared between parents (a message referenced by multiple conversations — unusual but possible)
- You need to paginate child records (you can't easily page through an embedded array)
- Child documents are large and you often don't need them (loading a conversation list shouldn't load all messages)

## The MongoDB 16MB document limit

A MongoDB document cannot exceed 16MB. For most chat apps with typical message sizes, a
conversation could hold thousands of messages before hitting this. But if:
- Messages are long (documents, code blocks)
- Conversations are long-lived (years of history)
- You store attachments or rich content

...then embedded documents will eventually hit the limit. A separate collection is safer
for high-volume or unbounded growth.

## How this maps to Clean Architecture

This is entirely an Infrastructure decision. The domain entity `Conversation` looks the same
regardless. `ChatMessage` entities look the same. The repository interface `IChatRepository`
looks the same.

Only `MongoChatRepository` and the document classes change. The domain and application layers
are completely unaffected by which strategy you choose.

This is the proof that the architecture is working: a storage design decision (embedded vs.
separate collection) has zero impact on business logic.

## Migrating from embedded to separate collection

If you start with embedded and later need to migrate:

1. Create a new `MongoChatRepository` implementation using a separate messages collection
2. Write a migration script to move embedded messages to the new collection
3. Swap the implementation in Program.cs
4. Domain, Application, and API code: zero changes

The ability to do this migration without touching business logic is exactly what Clean
Architecture is designed for.
