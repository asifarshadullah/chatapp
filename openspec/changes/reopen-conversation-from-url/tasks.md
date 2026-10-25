## 1. Backend: save the partial reply

- [ ] 1.1 Make the test repositories honour cancellation like MongoDB does: `AddMessageAsync` throws `OperationCanceledException` for a cancelled token, in `CountingChatRepository` (`Chat.Api.Tests`) and `FakeChatRepository` (`ChatServiceTests`). Verify the existing backend tests still pass
- [ ] 1.2 Write failing `ChatServiceTests`:
  - The provider throws after two tokens: the conversation holds the user's message and a reply equal to those two tokens joined, and the exception still reaches the caller.
  - The caller's token is cancelled after two tokens: the reply equal to those two tokens is saved.
  - The provider throws before any token: no reply is saved.

  Verify the first two fail today
- [ ] 1.3 In `ChatStreamingTests`, change `ModelFailsAfterFirstToken` and `CallerClosesPartWay` to expect the reply `"Hel"` saved after the user's message, and rename them from `...SavesNoReply` to `...SavesThePartialReply` (chat-streaming "The model fails after the first token", "The caller closes the request part-way through a reply"). The cancellation test waits, with `Patience`, for the reply to appear instead of reading once after the model sees cancellation. Verify both fail today
- [ ] 1.4 Wrap the token loop in `StreamResponseAsync` in `try`/`finally` and save the accumulated text, when non-empty, with `CancellationToken.None` (design.md). Verify 1.2 and 1.3 pass and `ModelFailsBeforeFirstToken` still sees no reply. Then run the mutation check and restore the code: pass `ct` to the save, and the cancellation tests in 1.2 and 1.3 fail

## 2. Frontend: load history

- [ ] 2.1 Write failing `chatApi` tests for `getHistory`:
  - A 200 response maps each record to a `ChatMessage` (`content` → `message`), in order.
  - A 404 reports "Conversation not found.".
  - A 500 and a network failure report "Your conversation could not be loaded.".
  - `SessionExpiredError` reports `SESSION_EXPIRED_MESSAGE`.
  - The request goes to `/api/chat/<id>/history` with `Authorization: Bearer`.
- [ ] 2.2 Rewrite `getHistory` to return `{ messages }` or `{ error }` as in design.md, exporting `CONVERSATION_NOT_FOUND_MESSAGE` and `HISTORY_LOAD_FAILED_MESSAGE`. Verify 2.1 passes and typecheck is clean

## 3. Frontend: the conversation in the URL

Each `ChatWindow` and `App` test starts from a clean URL (`history.replaceState(null, '', '/')` in `beforeEach`), since jsdom keeps it between tests.

- [ ] 3.1 Write failing `ChatWindow` tests for writing the URL:
  - After `onConversationId`, `location.search` holds `conversation=<id>`, `history.length` is unchanged, and another parameter and the fragment are kept.
  - A second message in the same conversation leaves the URL as it was.
- [ ] 3.2 Write failing `ChatWindow` tests for opening with `?conversation=<id>`, with `getHistory` mocked:
  - The saved messages are shown in order.
  - Until the history resolves, a loading indicator is shown instead of "What's on the agenda today?", and the input is disabled.
  - Unmounting and remounting, as the billing view does, shows the history again.
  - The next send passes that id to `streamMessage`.
  - Under StrictMode, each message is shown once.

  (conversation-links "Reloading the page reopens the conversation", "Loading the history", "Returning from the billing page reopens the conversation")
- [ ] 3.3 Write failing `ChatWindow` tests for history failures:
  - Not found: "Conversation not found." is shown, the parameter is removed, and the next send passes no conversation id (conversation-links "The URL names another user's conversation", "The URL names an unknown conversation").
  - Not an id: for `../../auth/sessions?x=`, `%2e%2e` and `abc`, "Conversation not found." is shown, `getHistory` is never called, and the parameter is removed (conversation-links "The URL names something that is not a conversation id").
  - Load failed: the message is shown, the parameter is kept, and the input stays disabled (conversation-links "The server cannot be reached while loading history").
  - Session expired: `onSessionExpired` is called and the parameter is kept (conversation-links "The session has ended while loading history").
- [ ] 3.4 Write failing `ChatWindow` tests for New chat:
  - It is absent on an empty chat.
  - It is disabled while a reply streams.
  - Choosing it clears the messages and the error, removes the parameter, and the next send passes no conversation id (conversation-links "Starting a new chat", "New chat is not offered during a reply").
  - After a failed load, it is shown, and choosing it enables the input (conversation-links "Leaving a conversation that failed to load").
- [ ] 3.5 Implement in `ChatWindow`: read the parameter on mount, load history with a cancelled flag, write the URL in `onConversationId` with `replaceState`, disable input while loading or after a failed load, and add the New chat header button. Verify 3.1–3.4 pass. Then run each mutation check and restore the code after each:
  - Use `pushState`: the `history.length` assertion fails.
  - Drop the id format check: the not-an-id test fails.

## 4. Frontend: signing out

- [ ] 4.1 Write failing `App` tests:
  - Logout removes the `conversation` parameter (conversation-links "The user signs out").
  - A session that ends by itself keeps it (conversation-links "The session ends by itself").
- [ ] 4.2 Remove the parameter in `App.endSession` only. Verify 4.1 passes

## 5. End to end (Playwright, real backend and model)

- [ ] 5.1 Add `chat.spec.ts` tests:
  - After a reply, the URL names a conversation, and a reload shows the same user message and a non-empty reply.
  - Opening `?conversation=<random guid>` shows "Conversation not found." and the URL loses the parameter.
  - New chat empties the window and removes the parameter.
  - In a conversation named in the URL, reloading during a reply after its first token shows the user's message, and a non-empty reply after it within a few reloads (conversation-links "Reloading during a reply keeps what was produced"). The retry is the known race in design.md, not flakiness to hide.

  Verify they fail on `main`'s frontend, then pass
- [ ] 5.2 Add a `session.spec.ts` test: a second user opening the first user's conversation URL sees "Conversation not found." and none of its messages. Verify it passes

## 6. Docs and full verification

- [ ] 6.1 Update the README's feature list and chat section for conversation links and the saved partial reply
- [ ] 6.2 Run `npm run typecheck`, `npm run lint` and `npm run test:run` in `frontend/chat-ui`, `dotnet test backend/ChatApp.sln`, `npx playwright test` in `e2e/playwright`, and `openspec validate --all --strict`. All must pass
