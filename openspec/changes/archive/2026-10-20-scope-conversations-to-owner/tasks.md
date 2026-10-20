## 1. Owner on the conversation

- [x] 1.1 In `ConversationTests`, add failing tests that `new Conversation(ownerId)` exposes `OwnerId` and that both constructors reject `Guid.Empty`; add `OwnerId` to `Conversation` and verify the tests pass
- [x] 1.2 Thread `userId` through the `IChatRepository` and `IChatService` signatures, the two repositories, both fakes, `ChatController` (passing `ICurrentUser.UserId`) and `ChatHub` (passing the `sub` claim from `Context.User`, for both the plan check and the service, and dropping `ICurrentUser`); remove `GetMessagesAsync` and its tests; the existing long-polling hub tests are the failing tests here, since they fail with "a conversation must have an owner" until the hub reads `Context.User`; verify `dotnet test backend/ChatApp.sln` is green
- [x] 1.3 In `ChatHubFeatureTests`, add a test that over long polling the plan check receives the signed-in caller's id, not `Guid.Empty`; confirm it fails when the hub reads `ICurrentUser.UserId` instead of `Context.User`, then restore

## 2. Repositories filter on owner

- [x] 2.1 In `InMemoryChatRepositoryTests`, add failing tests that a conversation is returned to its owner, returns `null` for another user, and that `AddMessageAsync` from another user throws `ConversationNotFoundException` and leaves the conversation unchanged; add `ConversationNotFoundException` to Chat.Application, implement the filter, and verify the tests pass
- [x] 2.2 Add the same tests to `MongoChatRepositoryTests`, plus one that a stored document with no `ownerId` field is unreachable to every user; add `OwnerId` to `ConversationDocument`, filter on id and owner in every query, and verify the tests pass with `docker compose up -d` running
- [x] 2.3 Add failing `ChatServiceTests` that sending into another user's conversation throws `ConversationNotFoundException` without calling the AI provider or creating a conversation, and that a `Guid.Empty` user id is rejected; implement in `ChatService` and verify the tests pass

## 3. API answers "not found"

- [x] 3.1 Make `TestAuthHandler` honour an optional `X-Test-User` header as the `sub` claim, defaulting to the existing `TestUserId`; verify the existing `Chat.Api.Tests` stay green
- [x] 3.2 In `ChatControllerTests`, add failing tests that Bob gets 404 for Alice's history and for `POST /api/chat` naming Alice's conversation, that Alice's history is unchanged and no new conversation was created afterwards, that `POST` with an unknown id returns 404 instead of 500, and that an empty message naming Alice's conversation still returns 400; catch `ConversationNotFoundException` in `ChatController.SendMessage` and verify the tests pass
- [x] 3.3 In `ChatHubTests`, add failing tests that Bob's `SendMessage` naming Alice's conversation, and one naming an unknown id, both fail with "Conversation not found." and send no `ReceiveConversationId` and no tokens, and that an empty message naming Alice's conversation still fails with "Message cannot be empty."; drive the enumerator by hand in `ChatHub` to translate the exception into a `HubException`, and verify the tests pass

## 4. Verification

- [x] 4.1 Run the CI checks (`npm run typecheck`, `npm run lint`, `npm run test:run` in `frontend/chat-ui`, and `dotnet test backend/ChatApp.sln`) and the Playwright suite in `e2e/playwright` locally, since the chat path changed; all must pass
- [x] 4.2 For each new test, break the owner check it guards on purpose and confirm the test fails, then restore the check
- [x] 4.3 After merge, clear the local development data with `db.conversations.deleteMany({})` in the `chatapp-mongodb` container, and confirm a fresh conversation round-trips in the running app

## 5. Close spec coverage gaps

- [x] 5.1 Let `TestAuthHandler` take an optional `X-Test-Role` header (default `User`), and add a test that an administrator gets 404 for another user's history; since no code grants administrators access, confirm the test fails when the owner filter is removed, then restore it
- [x] 5.2 Run the hub ownership tests over WebSockets as well as long polling, showing on both transports that a new conversation belongs to the caller and another user is refused; confirm the long-polling case fails when the hub reads `ICurrentUser` instead of `Context.User`, then restore it
- [x] 5.3 Strengthen the existing ownership tests: the hub cross-user send asserts no new conversation was created, both empty-message tests assert the owner's conversation is unchanged, and the API cross-user send asserts the AI model was never called (the fake AI provider counts calls); confirm each new assertion fails under a mutation that breaks what it guards, then restore. The empty-message assertions are protected in depth by three layers (the message check in the controller and hub, `ChatMessage` validation, and the owner filter), so no single-layer mutation can fail them; check them by breaking all three layers together
- [x] 5.4 Make the existing hub test `SendMessage_WithExistingConversationId_ContinuesSameConversation` wait for the conversation id instead of reading it the moment the stream ends, so it no longer fails intermittently when the id handler has not yet run; verify it passes in 40 consecutive runs
- [x] 5.5 Re-run the CI checks and the Playwright suite; all must pass
