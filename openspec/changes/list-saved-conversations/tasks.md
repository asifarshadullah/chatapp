## 1. The title rule

- [x] 1.1 Write failing tests in Chat.Application.Tests/Domain for `ConversationTitle.From` (conversation-titles "A new conversation is titled with its first message"):
  - `"  What is\n  a monad? "` gives `"What is a monad?"`.
  - Exactly 60 characters are kept whole.
  - Longer text is cut at the last space within the first 59 characters and ends with "…", and is never longer than 60.
  - 80 characters with no space give the first 59 and "…".
  - 58 letters followed by emoji give the 58 letters, one whole emoji and "…", which is valid UTF-16. This test fails with a `string.Length` cut.
  - Whitespace only gives an empty string.
- [x] 1.2 Write failing tests for `ConversationTitle.Clean` (conversation-titles "The model replaces the title after the first reply"):
  - A blank line, then `Title: “Understanding Monads.”`, then a second line, gives `Understanding Monads`.
  - Each of `"x"`, `'x'`, `“x”`, `‘x’` and `` `x` `` gives `x`.
  - `title: x` gives `x`.
  - Only one trailing full stop is removed, so `x..` gives `x.`.
  - Blank text, or text that is only quotes, gives an empty string.
  - A long answer is cut as `From` cuts.
- [x] 1.3 Implement `ConversationTitle.From` and `Clean` in Chat.Domain, counting and cutting by `Rune`, and add `Title` to `Conversation`, carried by the persistence constructor. Verify 1.1 and 1.2 pass and the backend builds

## 2. Storage, listing and the backfill

- [x] 2.1 Write failing `MongoChatRepositoryTests` (conversation-list "The caller's conversations are listed by latest activity", "Only conversations with a reply are listed", "The list is returned a page at a time"; conversation-ownership "Only the caller's own conversations are listed"):
  - `CreateConversationAsync(user, title)` stores the title, sets `LastActivityAt` to `CreatedAt`, and sets `HasReply` to false.
  - `AddMessageAsync` moves `LastActivityAt` to the message's timestamp. An assistant message sets `HasReply`, and a user message does not.
  - A conversation with only a user message is not listed. Once a reply is added, it is.
  - `LastActivityAt` read back is UTC.
  - A cursor built from user B's list, used by user A, returns only A's conversations.
  - `SetTitleAsync` changes the title for the owner, and throws not found for another user, whose conversation is left unchanged.
  - The list returns the owner's conversations newest first, with id, title and last activity only, and none of another user's.
  - Two conversations with equal `LastActivityAt` come back in the same order on every call.
  - With 25 conversations and a limit of 20, the first page has 20 and a cursor, and the second has 5 and none. Together they hold all 25 exactly once.
  - A cursor that is not base64url, has non-numeric ticks or has a non-Guid id throws a distinct invalid-cursor exception.
- [x] 2.2 Add `Title`, `LastActivityAt` and `HasReply` to `ConversationDocument`. Add `SetTitleAsync` and `ListConversationsAsync(userId, limit, cursor)`, which returns a page of `ConversationSummary(Id, Title, LastActivityAt)` and a next cursor, to `IChatRepository`. Implement them in `MongoChatRepository` with the keyset query and projection from design.md. Update the in-memory fakes. Verify 2.1 passes
- [x] 2.3 Write failing tests for `MongoChatRepository.EnsureIndexesAsync` (conversation-list "Conversations saved before the list existed are listed"):
  - It creates the `{ OwnerId: 1, LastActivityAt: -1, _id: -1 }` index with the partial filter `{ HasReply: true }`, and the list query's plan uses it, shown by `explain` reporting an `IXSCAN` on that index.
  - A document inserted as raw BSON with a user message and a reply, but no `Title`, `LastActivityAt` or `HasReply`, gets its first message's cut title, its latest message's timestamp and `HasReply: true`.
  - One with only a user message gets `HasReply: false` and is not listed.
  - One with no messages gets "New conversation", `CreatedAt` and `HasReply: false`.
  - One without `OwnerId` is untouched.
  - A second run changes nothing.
- [x] 2.4 Implement `EnsureIndexesAsync` as in design.md and call it at startup next to `MongoDailyMessageQuota.EnsureIndexesAsync`. Verify 2.3 passes

## 3. Titles from the model

- [x] 3.1 Write failing `OpenAiCompatibleProviderTests` for `CompleteAsync`, against the fake handler:
  - It sends `stream: false`, the option's model and its reasoning effort, the instruction as the only system message (not the chat system prompt), the content as one user message, and the given token cap.
  - It returns the reply's text.
  - A 429 throws `ModelRateLimitedException` after one request.
  - No error contains the key.
- [x] 3.2 Add `CompleteAsync` to `IAiProvider` and implement it in `OpenAiCompatibleProvider`. Extend `FakeChatCompletionsHandler` with a non-streamed response and update the three fake providers. Verify 3.1 passes
- [x] 3.3 Write failing tests for `ModelTitleGenerator` with a fake `IAiProvider` (conversation-titles "The model replaces the title after the first reply", "A title that cannot be generated leaves the first-message title"):
  - It asks the given model with the title instruction and the first message, cut to 1,000 characters, capped at `MaxTitleTokens`.
  - It cleans the answer with `ConversationTitle.Clean`: `"\"Understanding Monads\"\n"` gives `"Understanding Monads"`.
  - It returns null when nothing is left after cleaning.
  - It returns null when the provider throws `ModelRateLimitedException`, throws anything else, or has not answered after the timeout. For the timeout test, inject a short timeout.
- [x] 3.4 Add `ITitleGenerator` to Chat.Application. Implement `ModelTitleGenerator` in Chat.Infrastructure, and add `Ai:MaxTitleTokens` (default 300) to `AiSettings`, with `AiSettingsValidator` refusing values below 1. Write a failing startup test showing that 0 refuses to start, then make it pass. Register the generator as a singleton. Verify 3.3 and the startup test pass

## 4. The chat service

- [x] 4.1 Write failing `ChatServiceTests` with a fake `ITitleGenerator` (conversation-titles, all requirements):
  - A new conversation is created with `ConversationTitle.From` of the message.
  - After its reply streams to the end, the generator is called once with the model id and the message, and its title is stored before the enumeration completes.
  - A null from the generator leaves the first-message title.
  - A second message into the conversation does not call the generator.
  - A provider that throws after the first token, or a cancelled enumeration, does not call the generator, and a later complete reply in that conversation does not call it either.
  - When the caller's token is cancelled after the last token, while the generator is running, the generator gets `CancellationToken.None` and the title is still stored.
  - The quota is consumed exactly once for a new conversation that gets titled.
  - The history after titling holds only the user's message and the reply.
- [x] 4.2 Implement it in `ChatService` as in design.md decision 4, and add `ListConversationsAsync(userId, limit, cursor)` passing through to the repository. Verify 4.1 passes and the existing `ChatServiceTests` still pass

## 5. The API

- [x] 5.1 Write failing tests in Chat.Api.Tests (conversation-list, all requirements; conversation-titles "The model replaces the title after the first reply"):
  - `GET /api/chat/conversations` returns `{ conversations: [{ id, title, lastActivityAt }], nextCursor }`, newest first, with no messages.
  - A user with none gets an empty list and a null `nextCursor`.
  - Following `nextCursor` through 25 conversations returns each once.
  - `limit=0` and `limit=51` give 400 "Limit must be between 1 and 50.", and `limit=abc` gives 400.
  - Cursors that are not base64url, decode to non-numeric ticks, or decode to a non-Guid id give 400 "Invalid cursor.".
  - `lastActivityAt` in the JSON ends with `Z`.
  - When the fake provider fails before its first token (502) or is rate limited (503), the new conversation does not appear in the list. When it fails after the first token, it does.
  - After a first message streams to `done` with the fake generator returning "Test Title", the list shows "Test Title". When the generator fails, the stream still ends with `done`, has no `error` event, and the list shows the first-message title.
- [x] 5.2 Add the endpoint and its response contract to `ChatController`, with `[Authorize(Policy = "CanReadConversation")]` and the limit and cursor checks. Register a fake `ITitleGenerator` in both API test factories. Verify 5.1 passes
- [x] 5.3 Write failing tests in Chat.Identity.Tests, where authentication and roles are real (chat-access "Listing conversations requires the read permission"; conversation-ownership "Only the caller's own conversations are listed"):
  - Without a token, the list returns 401.
  - A role without `conversation:read` gets 403.
  - A user whose plan lacks chat still gets their list.
  - User A's list holds none of B's conversations.
  - An Admin's list holds only the Admin's own.

  Verify they pass with the endpoint from 5.2, and that each fails when the policy attribute or the owner filter is removed

## 6. The sidebar

- [x] 6.1 Write failing tests for `listConversations` in `chatApi`:
  - It calls `/api/chat/conversations` with `cursor` only when given.
  - It returns the parsed page.
  - A non-OK response or a network error gives "Your conversations could not be loaded.", and a session error gives the session-expired message.
- [x] 6.2 Implement `listConversations`. Verify 6.1 passes
- [x] 6.3 Write failing `ConversationSidebar` tests (conversation-sidebar "The chat shows the user's conversations in a sidebar", "The sidebar shows loading, empty and failed states", "More conversations load on request"):
  - It shows a loading state, then the titles in order.
  - It shows "No conversations yet." when the list is empty.
  - On failure it shows "Your conversations could not be loaded.", and Retry reloads.
  - "Show more" appends the next page, with no duplicates, and disappears on the last page.
  - A failed "Show more" keeps the entries, shows "More conversations could not be loaded." and keeps the button. Choosing it again loads the page.
  - A title `<b>bold</b>` shows as that text.
  - The current conversation is marked, for example with `aria-current`.
  - New chat is always shown. It is disabled when told the chat is empty or a reply is streaming, and the entries are disabled while a reply is streaming.
- [x] 6.4 Implement `ConversationSidebar`. Verify 6.3 passes
- [x] 6.5 Write failing `ChatShell` tests (conversation-sidebar "Choosing a conversation opens it", "The list follows the user's activity"; conversation-links "New chat starts an empty conversation"):
  - Choosing an entry puts its id in the URL without a history entry, and shows its history.
  - A message then sent goes to that conversation.
  - New chat in the sidebar empties the chat and removes the id.
  - Choosing the open conversation's entry makes no history request and keeps the chat as it is.
  - After a send finishes, by `done` or by an error, the first page is reloaded and merged so that a moved entry shows once, at the top.
  - A failed reload keeps the entries and shows no error.
  - Below 900 px the sidebar is hidden behind a menu button, and choosing an entry or New chat closes it.
  - A session-expired list error, on any load, calls `onSessionExpired`.
  - Signing out as A and in as B never shows A's titles: render the app, sign out, sign in with B's list mocked, and assert that A's titles are absent at every step.

  Move the existing `ChatWindow` New chat tests to the shell. Replace "offers no new chat on an empty chat" with "shows New chat disabled on an empty chat" (conversation-links "New chat starts an empty conversation")
- [x] 6.6 Implement `ChatShell` as in design.md decision 6, render it from `App`, add the four callbacks to `ChatWindow`, and remove `ChatWindow`'s own New chat. Verify 6.5 passes and `npm run typecheck`, `npm run lint` and `npm run test:run` are clean

## 7. End to end and docs

- [x] 7.1 Add a Playwright test to `chat.spec.ts`:
  1. Send a message and wait for the reply.
  2. Check that an entry appears in the sidebar.
  3. Choose New chat.
  4. Choose that entry.
  5. Check that the URL names it and its messages are shown.

  Run `npx playwright test` against local Ollama and verify it passes with the rest
- [x] 7.2 Document `GET /api/chat/conversations` and `Ai:MaxTitleTokens` in the README. Verify `openspec validate --all --strict` passes
- [x] 7.3 Run the CI suites: `dotnet test backend/ChatApp.sln`, and in `frontend/chat-ui` `npm run typecheck`, `npm run lint` and `npm run test:run`. Then check by hand with `scripts/with-groq.sh dotnet run` that a new conversation gets a model title from `gpt-oss-120b`, and tune the instruction and `MaxTitleTokens` if the titles come back empty
