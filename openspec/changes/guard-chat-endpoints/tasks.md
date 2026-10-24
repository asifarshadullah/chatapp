## 1. Keep the coverage the HTTP send path carried

- [ ] 1.1 Add `StreamResponseAsync` service tests that null, empty and whitespace content are rejected with `ArgumentException`, before any conversation is created or the AI is called. The method is a lazy async iterator, so each test must enumerate it (the exception surfaces on the first `MoveNextAsync`, not on the call). They pass against current code; verify each fails when the `ChatMessage` construction in `StreamResponseAsync` is moved after the repository call (mutation check), then restore it
- [ ] 1.2 Add a `StreamResponseAsync` service test that a message into an existing conversation is appended to it (user and assistant messages added, no new conversation). Verify it fails when the existing-conversation branch creates a new conversation instead (mutation check)
- [ ] 1.3 Rewrite the `ChatControllerTests` history tests to seed conversations through `factory.Repository` (`CreateConversationAsync`, `AddMessageAsync`), not `POST /api/chat`. Verify the history tests still pass, including own history, unknown id → 404 and the existing ownership 404s (conversation-ownership "Owner reads their own history", "Another user requests the history", "An administrator requests another user's history", "Unknown id on history")

## 2. Remove the HTTP send path (chat-access: "Sending a message over HTTP")

- [ ] 2.1 Write a failing controller test: `POST /api/chat` with a valid message → 404, `factory.Ai.Calls` unchanged, `factory.Repository.ConversationsCreated` unchanged. Verify it fails today (200)
- [ ] 2.2 Delete `ChatController.SendMessage`, `IChatService.SendMessageAsync`, `ChatService.SendMessageAsync`, `ChatRequestDto` and `ChatResponseDto`, along with the controller's POST tests and the service's `SendMessageAsync` tests. Verify the test from 2.1 passes and `dotnet build backend/ChatApp.sln` has no references left
- [ ] 2.3 Point `Chat.Identity.Tests` `Unauthenticated_SendMessage_Returns401` at `GET /api/chat/{id}/history` and rename it to match. Verify it returns 401 and fails if the controller's `[Authorize]` is removed (mutation check)

## 3. Require `conversation:read` for history (Chat.Identity.Tests, real permission service)

- [ ] 3.1 Add the test-only roles `NoRead` (`conversation:create` only) and `NoCreate` (`conversation:read` only) to `AuthApiFactory`'s `FakeRoleStore`, and a way for tests to seed a conversation for a user through the factory's `IChatRepository`. Verify the existing `Chat.Identity.Tests` still pass
- [ ] 3.2 Write failing tests as `NoRead`: own history → 403 with no messages in the body (chat-access "Caller without the read permission requests their own history"); another user's conversation → 403 and an unknown id → 403 (chat-access "…another user's or an unknown conversation"). Verify they fail today (200, 404, 404)
- [ ] 3.3 Write tests as `User` and as `Admin`: own history → 200 with every message (chat-access "Caller with the read permission reads their own history", "A role that grants every permission can read"). They pass today; verify the `Admin` test fails when `PermissionService`'s `*` wildcard check is removed (mutation check), then restore it
- [ ] 3.4 Register `CanReadConversation` → `PermissionRequirement("conversation:read")` in `Program.cs` and add `[Authorize(Policy = "CanReadConversation")]` to `GetHistory`. Verify 3.2 and 3.3 pass, then mutation-check: remove the attribute (3.2 fails), and point the policy at `conversation:create` (3.2 fails), restoring each

## 4. Pin down the hub's existing gates

- [ ] 4.1 In `Chat.Identity.Tests`, write a test that `NoCreate` opening the real-time connection is refused with 403 (chat-access "Caller without the create permission opens the real-time connection"). It passes today; verify it fails when `[Authorize(Policy = "CanChat")]` is removed from `ChatHub` (mutation check), then restore it
- [ ] 4.2 Tighten `ChatHubFeatureTests.SendMessage_WhenChatFeatureDisabled_ThrowsHubException` to expect the exact error "Chat is not available on your current plan." and no AI call (`factory.Ai.Calls` is 0) (chat-access "Caller whose plan does not include chat…"). Verify it fails when the plan check in `ChatHub` is removed (mutation check), then restore it

## 5. Remove the unused frontend send call

- [ ] 5.1 Delete `sendMessage` from `chatApi.ts`, the `ChatResponse` type from `types/chat.ts` and `chatApi.test.ts`. Verify `npm run typecheck`, `npm run lint` and `npm run test:run` pass in `frontend/chat-ui`

## 6. Verify

- [ ] 6.1 Confirm the conversation-ownership scenarios still have passing tests now that their HTTP twins are gone: the hub tests for a new conversation's owner (both transports), another user sending, an unknown id, and an empty message into another user's conversation, plus the history 404s. Note the test for each scenario
- [ ] 6.2 Run `dotnet test backend/ChatApp.sln`, the frontend CI suites, `npx playwright test` in `e2e/playwright` (chat path touched), and `openspec validate guard-chat-endpoints --strict`. All pass
