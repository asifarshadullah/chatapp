# Iteration 4: In-Memory Conversation History

## Goal
Maintain conversation history per session so the chat remembers previous messages within a conversation.
Uses in-memory storage — persistence to MongoDB comes in Iteration 5.

## Context
Currently, each POST `/api/chat` is stateless — it echoes one message with no memory.
This iteration adds the concept of a Conversation that tracks all messages exchanged.

## Prerequisites
- Iterations 1–3 complete — backend, frontend, and E2E tests all working

---

## Design decisions
- A `Conversation` entity holds an ordered list of `ChatMessage` objects
- The API uses a `ConversationId` (GUID) — returned in the first response, sent in subsequent requests
- In-memory storage via `ConcurrentDictionary<Guid, Conversation>` (thread-safe)
- `IChatRepository` interface in Application layer; `InMemoryChatRepository` in Infrastructure
- Frontend stores `conversationId` in component state (lost on page refresh — that's OK for now)

---

## Phase 1: Backend — Domain + Repository (TDD — vertical slices)

One test → minimal impl → next test. Never write the full test list before implementing.

**File:** `backend/tests/Chat.Application.Tests/Domain/ConversationTests.cs`
**File:** `backend/src/Chat.Domain/Entities/Conversation.cs`

### Domain entity cycles (Conversation)
- Cycle 1: RED `Create_GeneratesValidId` → GREEN scaffold `Conversation` with auto `Guid` Id
- Cycle 2: RED `Create_StartsWithEmptyMessageList` → GREEN init empty internal list
- Cycle 3: RED `AddMessage_AppendsToMessageList` → GREEN implement `AddMessage`
- Cycle 4: RED `AddMessage_WithNullMessage_ThrowsArgumentNullException` → GREEN add null guard
- Cycle 5: RED `GetMessages_ReturnsMessagesInChronologicalOrder` → GREEN return ordered list
- Refactor

Properties: `Id` (Guid), `CreatedAt` (DateTime), `Messages` (IReadOnlyList — encapsulated).

### Repository cycles (InMemoryChatRepository)
**File:** `backend/tests/Chat.Infrastructure.Tests/Repositories/InMemoryChatRepositoryTests.cs`
**File:** `backend/src/Chat.Infrastructure/Repositories/InMemoryChatRepository.cs`

- Cycle 1: RED `CreateConversationAsync_ReturnsNewConversation` → GREEN scaffold `IChatRepository` interface + `InMemoryChatRepository` with `ConcurrentDictionary`
- Cycle 2: RED `GetConversationAsync_WithValidId_ReturnsConversation` → GREEN lookup by id
- Cycle 3: RED `GetConversationAsync_WithInvalidId_ReturnsNull` → GREEN return null on miss
- Cycle 4: RED `AddMessageAsync_StoresMessageInConversation` → GREEN append message
- Cycle 5: RED `AddMessageAsync_WithInvalidConversationId_ThrowsKeyNotFoundException` → GREEN guard missing id
- Cycle 6: RED `GetMessagesAsync_ReturnsAllMessagesInOrder` → GREEN return ordered messages
- Refactor

### Task 1.4 — IChatRepository interface + DI note
**Interface:** `backend/src/Chat.Application/Interfaces/IChatRepository.cs`
```csharp
public interface IChatRepository
{
    Task<Conversation> CreateConversationAsync(CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task AddMessageAsync(Guid conversationId, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default);
}
```

**Implementation:** `backend/src/Chat.Infrastructure/Repositories/InMemoryChatRepository.cs`
- Use `ConcurrentDictionary<Guid, Conversation>` for thread-safe storage
- Register as **Singleton** in DI (in-memory state must persist across requests)

---

## Phase 2: Backend — Updated Service + Controller (TDD — vertical slices)

### ChatService cycles
**File:** `backend/tests/Chat.Application.Tests/Services/ChatServiceTests.cs`

- Cycle 2.1: RED `SendMessageAsync_WithNoConversationId_CreatesNewConversation` → GREEN inject `IChatRepository`, create conversation when id is null
- Cycle 2.2: RED `SendMessageAsync_WithExistingConversationId_AppendsToConversation` → GREEN look up existing conversation
- Cycle 2.3: RED `SendMessageAsync_StoresBothUserAndAssistantMessages` → GREEN store both messages in repo
- Cycle 2.4: RED `SendMessageAsync_ReturnsConversationIdInResponse` → GREEN include id in response DTO
- Refactor

### Task 2.2: Update DTOs
**New/Updated DTOs:**
- `ChatRequestDto` — add optional `ConversationId` property
- `ChatResponseDto` — add `ConversationId` property
- `ConversationHistoryDto` — `{ ConversationId, Messages[] }`

### Task 2.3: GREEN — Update ChatService
Inject `IChatRepository`. Logic:
1. If no ConversationId provided → create new conversation
2. Store user's message in conversation
3. Generate echo response
4. Store echo response in conversation
5. Return response with ConversationId

### Controller integration cycles
**File:** `backend/tests/Chat.Api.Tests/Controllers/ChatControllerTests.cs`

- Cycle 2.4: RED `PostMessage_ReturnsConversationIdInResponse` → GREEN update controller to pass conversationId through
- Cycle 2.5: RED `PostMessage_WithConversationId_ContinuesConversation` → GREEN wire optional conversationId in request body
- Cycle 2.6: RED `GetHistory_WithValidId_ReturnsAllMessages` → GREEN add `GET /api/chat/{id}/history`
- Cycle 2.7: RED `GetHistory_WithInvalidId_ReturnsNotFound` → GREEN return 404 on missing id
- Cycle 2.8: RED `GetHistory_AfterTwoMessages_ReturnsFourMessages` → GREEN confirm message count (2 user + 2 echo)
- Refactor

### Task 2.5 — Update ChatController (note)
Add endpoints:
- Update `POST /api/chat` — accept optional `conversationId` in body, return it in response
- Add `GET /api/chat/{conversationId}/history` — returns all messages for a conversation

### Task 2.6: Update DI registration
In `Program.cs`:
- Register `IChatRepository` → `InMemoryChatRepository` as **Singleton**
- Update `IChatService` → `ChatService` (now depends on IChatRepository)

---

## Phase 3: Frontend Updates

### Task 3.1: Update API client
**File:** Update `frontend/chat-ui/src/services/chatApi.ts`

- `sendMessage(message, conversationId?)` — include conversationId in request body
- `getHistory(conversationId)` — new function, calls GET endpoint

### Task 3.2: Update ChatWindow state management
- Store `conversationId` from first API response
- Pass `conversationId` in subsequent requests
- On page refresh: start a new conversation (no persistence yet)

### Task 3.3: Update component tests
Verify that conversationId is sent correctly after first message.

---

## Phase 4: Update E2E Tests

### Task 4.1: Update existing E2E tests
Ensure existing tests still pass with the new conversation-based API.

### Task 4.2: Add conversation-specific E2E test
```
Test: "messages persist within a conversation session"
1. Send "Message 1" → get response
2. Send "Message 2" → get response
3. Verify all 4 messages visible in correct order
4. Verify messages have consistent conversation context
```

---

## Acceptance criteria
1. All backend tests pass (unit + integration)
2. All frontend component tests pass
3. All E2E tests pass
4. First POST creates a new conversation and returns ConversationId
5. Subsequent POSTs with ConversationId append to the same conversation
6. GET `/api/chat/{id}/history` returns complete message history
7. GET with invalid ConversationId returns 404
8. Frontend automatically tracks ConversationId within a session
9. Page refresh starts a new conversation

## Verification commands
```bash
# Backend tests
dotnet test backend/ChatApp.sln --verbosity normal

# Frontend tests
cd frontend/chat-ui && npm test

# E2E tests
cd e2e/playwright && npx playwright test

# Manual verification — send multiple messages, check history:
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"First message"}'
# Note the conversationId from the response, then:
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Second message","conversationId":"<id-from-above>"}'
# Get history:
curl http://localhost:5000/api/chat/<id>/history
```

## What you will learn
- Repository pattern — abstracting data access behind an interface
- `ConcurrentDictionary` for thread-safe in-memory storage
- Stateful conversations in a REST API
- Preparing for database persistence (Iteration 5) with clean interfaces
