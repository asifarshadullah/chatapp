## 1. Test support

- [ ] 1.1 Add an SSE reader to `Chat.Api.Tests` that posts with `HttpCompletionOption.ResponseHeadersRead` and yields `(event, data)` pairs as they arrive, and a scriptable AI provider for `ChatApiFactory`. The provider can yield given tokens, wait on a signal between tokens, throw after a given token, and record whether its cancellation token fired. Verify the existing `Chat.Api.Tests` still pass with the factory change

## 2. The streaming endpoint (backend, alongside the hub)

- [ ] 2.1 Write failing tests for `POST /api/chat/messages` without a conversation id (chat-streaming "A message streams its reply", conversation-ownership "Sending without a conversation id starts a conversation owned by the sender"). Expect 200, `text/event-stream`, a first `conversation` event with the new id, `token` events in the model's order, and a final `done`. The conversation must be readable by the caller and not by another user. Verify they fail today (404)
- [ ] 2.2 Write a failing test that after `done` the history holds the user's message and a reply equal to the streamed tokens joined in order (chat-streaming "The saved reply matches the streamed tokens")
- [ ] 2.3 Write a failing test that a token containing `\n` arrives as one `token` event with the line break intact (chat-streaming "A token containing a line break")
- [ ] 2.4 Write a failing test that continuing a conversation the caller owns streams that conversation's id and adds the message and reply to it (conversation-ownership "Owner continues their conversation")
- [ ] 2.5 Add the `SendMessage` action to `ChatController` with `[Authorize(Policy = "CanChat")]`, the SSE writer from design.md (named events, JSON `data`, flush per event, buffering disabled, `Cache-Control: no-cache`, `X-Accel-Buffering: no`), and the request's cancellation token passed to `StreamResponseAsync`. Verify 2.1–2.4 pass, then mutation-check 2.3 by writing the token raw instead of as JSON (fails), restoring it

## 3. Refusals before the stream

- [ ] 3.1 Write failing tests that an empty message and an over-long message, each naming another user's conversation, get 400 with "Message cannot be empty." and a too-long error in `detail`. Cover `""`, whitespace and an omitted `message` field for the empty case. Expect a non-event-stream body, no AI call, and the owner's conversation unchanged (conversation-ownership "Empty message naming another user's conversation", "Over-long message naming another user's conversation"; chat-streaming "A message refused for its content, plan or conversation"). Verify the `""` and omitted cases fail with "The Message field is required." when the request's `Message` is declared non-nullable (mutation check), then restore it
- [ ] 3.2 Write failing tests that sending into another user's conversation and sending into an unknown id both get 404 with "Conversation not found.", identical bodies, and no `conversation` or `token` event. In both cases expect no AI call and no conversation created, and for another user's conversation, the owner's conversation unchanged (conversation-ownership "Another user sends into the conversation", "Unknown id when sending")
- [ ] 3.3 Write a failing test on `DisabledChatFeatureFactory` that sending gets 403 with "Chat is not available on your current plan." in `detail`, no AI call and no conversation created (chat-access "Caller whose plan does not include chat sends a message")
- [ ] 3.4 Implement the checks in design.md's order (content, length, plan, then the first `MoveNextAsync` with `ConversationNotFoundException` → 404) before any header is written. Verify 3.1–3.3 pass. Then run each mutation check below and restore the code after each:
  - Write the headers before the first `MoveNextAsync`: 3.2 fails.
  - Remove the plan check: 3.3 fails.

## 4. Streaming behaviour

- [ ] 4.1 Write a test with the scriptable provider holding after the first token: the reader receives `conversation` and the first `token` before the provider is released (chat-streaming "The first token arrives before the reply is finished"). Verify it fails when the per-event flush is removed and buffering left on (mutation check), then restore it
- [ ] 4.2 Write a failing test with the provider throwing after the first token. Expect `conversation`, one `token`, then `error` with "The reply could not be completed." and no exception text, no `done`, and the history holding only the user's message (chat-streaming "The model fails after the first token")
- [ ] 4.3 Write a failing test with the provider throwing before any token. Expect 502 with "The reply could not be completed." in `detail`, no exception text or stack trace in the body, no event stream, and no reply in the history (chat-streaming "The model fails before its first token"). Run it with the factory's environment set to Development, so ASP.NET Core's error page would show if the exception escaped
- [ ] 4.4 Write a failing test that closing the response after the first token fires the provider's cancellation token, and the history holds only the user's message (chat-streaming "The caller closes the request part-way through a reply")
- [ ] 4.5 Catch failures:
  - Before the first item: log, and answer 502 with the fixed message.
  - After the first event: log, and write the fixed `error` event.
  - When the request was aborted: write nothing.

  Verify 4.2–4.4 pass. Then run two mutation checks, restoring the code after each:
  - Remove the catch before the first item: 4.3 fails.
  - Pass `CancellationToken.None` to `StreamResponseAsync`: 4.4 fails.

## 5. Permissions with the real permission service (`Chat.Identity.Tests`)

- [ ] 5.1 Expose the fake AI provider's call count from `AuthApiFactory`. Replace the two negotiate tests in `ChatAccessTests` with POSTs to `/api/chat/messages`:
  - `NoCreate` gets 403, no AI call and no event stream (chat-access "Caller without the create permission sends a message"; chat-streaming "A message refused for sign-in or permission").
  - `User` gets 200 with an event stream (chat-access "Caller with the create permission sends a message").
  - No token gets 401 and no AI call (chat-access "Caller who is not signed in sends a message").
- [ ] 5.2 Verify 5.1's tests pass, then run two mutation checks, restoring the code after each:
  - Remove `[Authorize(Policy = "CanChat")]` from the action: the `NoCreate` test fails.
  - Remove the class's `[Authorize]` along with it: the 401 test fails.

## 6. Frontend: send over the stream

- [ ] 6.1 Write failing `chatApi` tests for `streamMessage`, with `fetch` stubbed to return a `ReadableStream`:
  - Events split across chunk boundaries reach `onConversationId`, `onWord` and `onComplete` in order.
  - A token containing `\n` arrives intact.
  - An `error` event reaches `onError` with its message.
  - A stream that ends without `done` reports "The reply was interrupted.".
  - An error status reports the `ProblemDetails` `detail`, or "Your message could not be sent." when there is no body.
  - A network failure reports `CONNECT_FAILED_MESSAGE`.
  - `SessionExpiredError` reports `SESSION_EXPIRED_MESSAGE`.
  - An aborted signal reports nothing.
  - The request carries `Authorization: Bearer` and no `access_token` in the URL.
- [ ] 6.2 Write a failing test that `streamMessage` called while a token renewal is pending does not call `fetch` until the renewal resolves, then sends with the renewed token (token-refresh "The streaming connection waits for renewal")
- [ ] 6.3 Implement `streamMessage(message, conversationId, callbacks, signal)` in `chatApi.ts` on `authorizedFetch` with the parser from design.md. Move `StreamCallbacks` with it. Verify 6.1 and 6.2 pass
- [ ] 6.4 Rewrite the `ChatWindow` tests against a mocked `streamMessage` and `authService.getValidToken`:
  - Mount with `SessionExpiredError` shows `SESSION_EXPIRED_MESSAGE` and calls `onSessionExpired` (token-refresh "Opening the chat with a token that can no longer be renewed").
  - Any other error on mount shows nothing.
  - Unmounting mid-reply aborts the signal.
  - The existing send, render and error tests keep their expectations.

  Verify the mount and abort tests fail against the current `ChatWindow`.
- [ ] 6.5 Switch `ChatWindow` to `getValidToken()` on mount and to `streamMessage` with an `AbortController` per send, aborted on unmount. Remove `signalRService.stop()` from `App.tsx` and update `App.test.tsx` to mock the new modules, keeping its session-expiry expectations. Verify `npm run typecheck`, `npm run lint` and `npm run test:run` pass

## 7. Remove SignalR

- [ ] 7.1 Write a failing test in `ChatAccessTests` that `POST /chatHub/negotiate?negotiateVersion=1` as `User` gets 404 (chat-access "Opening a real-time chat connection"). Verify it fails today (200)
- [ ] 7.2 Delete `Hubs/ChatHub.cs`, `ChatHubTests`, `ChatHubFeatureTests`, and the `Microsoft.AspNetCore.SignalR.Client` test package. Before deleting `ChatHubFeatureTests`, move `GetHistory_WhenChatFeatureDisabled_StillReturnsOwnHistory`, `DisabledChatFeatureFactory` and the recording plan factory to a controller test file. Remove `AddSignalR`, `MapHub` and the `access_token` `OnMessageReceived` hook from `Program.cs`, and change the CORS comment to name the refresh cookie. Verify 7.1 passes and `dotnet build backend/ChatApp.sln` has no SignalR references left
- [ ] 7.3 Write a test that a plan check on send receives the signed-in caller's id, replacing `SendMessage_OverLongPolling_ChecksThePlanOfTheSignedInCaller`. Verify it fails when the action passes `Guid.Empty` (mutation check), then restore it
- [ ] 7.4 Delete `signalRService.ts`, its test, `@microsoft/signalr` from `package.json` (with the lockfile updated by `npm uninstall`), and the `/chatHub` proxy in `vite.config.ts`. Update the comments in `sessionErrors.ts` and `authServiceRefresh.test.ts` that name `signalRService`. Verify `grep -ri signalr frontend/chat-ui/src frontend/chat-ui/package.json frontend/chat-ui/vite.config.ts` finds nothing and the frontend CI suites pass

## 8. Docs

- [ ] 8.1 Write `docs/architecture/decisions/0002-stream-chat-replies-over-sse.md` in the style of 0001: the context, the decision, the alternatives (keep SignalR, SignalR forced onto its SSE transport, `fetch-event-source`), and the consequences, including #20 no longer needing a backplane. Verify it links #24
- [ ] 8.2 Update the README's badge, feature list, architecture diagram, streaming sequence diagram, tech table and the "SignalR server streaming" section to describe the SSE endpoint. Verify `grep -i signalr README.md` finds only the ADR's history, if anything

## 9. Verify

- [ ] 9.1 Map every scenario in the four delta specs to its test, including the unchanged token-refresh scenarios and their existing tests, and note the test for each. Confirm the scenarios that are not new still pass: "Unknown id on history", and the e2e test "a session ending by itself leaves the credential exchangeable"
- [ ] 9.2 Run `dotnet test backend/ChatApp.sln`, `npm run typecheck`, `npm run lint` and `npm run test:run` in `frontend/chat-ui`, `npx playwright test` in `e2e/playwright`, and `openspec validate stream-chat-over-sse --strict`. All pass
- [ ] 9.3 Manually check, in the browser's network panel, that the reply arrives as separate `token` events through the Vite proxy, not in one chunk at the end
