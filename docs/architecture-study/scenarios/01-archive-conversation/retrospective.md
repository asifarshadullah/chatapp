# Scenario 01: Retrospective

## What the plan got right

- Invariant correctly placed in `Conversation.AddMessage()` — impossible to bypass
- `Archive()` correctly identified as a domain method (no I/O)
- Workflow (`load → Archive() → save`) correctly identified as application concern
- `IsArchived` correctly identified as a flag on the existing entity, not a new entity

## What the plan missed or got wrong initially

### The reconstruction conflict

Initial plan had `ToConversation()` calling `conversation.Archive()` then adding messages.
This failed because `AddMessage()` checks `IsArchived` — reconstructing an archived
conversation with existing messages was blocked by the domain's own rule.

**Lesson:** Domain invariants written for mutation can conflict with the reconstruction path.
The solution is a reconstruction constructor that trusts incoming data and bypasses guards.
Always ask during design: *"Can I faithfully reconstruct this entity from stored data without
tripping its own invariants?"*

### The reconstruction constructor expansion

Adding `IsArchived` to the reconstruction constructor forced a decision: do you also pass
messages in, or reconstruct them separately? Passing them directly (bypassing `AddMessage`)
was cleaner because it made the reconstruction intent explicit.

**Lesson:** Reconstruction constructors tend to grow over time as entities gain more fields.
That is expected — keep them clearly labelled with a comment.

## Questions this scenario raised

1. What if we want to allow unarchiving in the future? The current `Archive()` is one-way.
   Would that be `Unarchive()` on the entity, or a separate status enum (`Active`, `Archived`)?

2. Should `ArchiveConversationAsync` return something? Currently void. If the client wants
   to show "archived at timestamp", the service needs to return a DTO.

3. If two requests archive the same conversation simultaneously, is there a race condition?
   (In MongoDB: the `UpdateOneAsync` is atomic at the document level, so IsArchived = true
   would just be written twice — safe, since Archive() is idempotent.)

## Practice prompts to extend this scenario

- Add an `ArchivedAt` timestamp to the domain. Where does it go? Does the reconstruction
  constructor need to change again?
- Add a filter to `GetHistoryAsync` so it can optionally include/exclude archived conversations.
  Which layer does that filter logic live in?
- Add a domain event `ConversationArchivedEvent` that fires when a conversation is archived.
  Who raises it? Who handles it? Where does the handler live?
