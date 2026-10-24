## Why

Chat replies stream over a SignalR hub, but the app uses almost none of what SignalR is for
(#24). The hub has one method, and it streams one reply back to the one client that asked:
a request with a streamed response, which is what server-sent events are for. Keeping
SignalR costs a token in the query string (`?access_token=`), where it can end up in URLs
and logs; a workaround to find the caller under long polling; a client and server package
on each side; and, to run more than one instance, a backplane that exists only because
connections live in one process.

## What Changes

- **BREAKING** Replace the chat hub with `POST /api/chat/messages`, which takes a message
  and an optional conversation id and streams the reply as `text/event-stream`: the
  conversation id first, then the reply's tokens in order, then an end marker.
- The endpoint makes every check the hub makes, in the same order: the
  `conversation:create` permission, the message's content, the caller's plan, then the
  conversation. A refusal is an ordinary HTTP error (401, 403, 400 or 404) sent before any
  part of the stream, so a refused caller never receives a conversation id or a token.
- If the model fails before its first token, the answer is an ordinary 502 error. A
  failure after the reply has started ends the stream with an error event. Neither reveals
  internal detail.
- Closing the request stops the reply.
- **BREAKING** Remove `/chatHub`, SignalR on both sides, the `?access_token=` query-string
  token, and the `/chatHub` WebSocket proxy in the dev server. The token travels in the
  `Authorization` header like every other request.
- The frontend sends through the existing `authorizedFetch`, so token renewal and the
  retry after a 401 apply to sending as they do to reading history.
- The chat window still checks the session when it opens, so a session that has ended is
  still sent to sign in on load and not on the first send. Today the hub connection does
  this as a side effect.
- Record the decision in ADR 0002 and update the README's streaming sections.

## Capabilities

### New Capabilities

- `chat/chat-streaming`: how a reply is delivered: the order of events, refusals before
  the stream starts, a failure before the first token or part-way through, the reply
  arriving token by token, and stopping when the caller goes away.

### Modified Capabilities

- `chat/chat-access`: messages are sent only through the streaming endpoint, not a
  real-time connection. A caller without the create permission or without chat on their
  plan is refused by that endpoint. The scenario that a plain HTTP POST to the chat API is
  not found goes away, because sending is now an HTTP POST.
- `chat/conversation-ownership`: its sending scenarios name the streaming endpoint and its
  HTTP responses instead of the real-time connection and its errors. The scenario that
  ownership holds however the real-time connection is carried goes away with that
  connection.
- `identity/token-refresh`: the scenario where the chat connection waits for a token
  renewal now covers sending a message. A new scenario requires the chat to check the
  session when it opens, which the hub connection used to do as a side effect.

## Impact

- **Backend:** `ChatController` (new streaming action), `Program.cs` (SignalR, its token
  hook and hub mapping removed), `Hubs/ChatHub.cs` (deleted), and the tests that use the
  hub: `ChatHubTests`, `ChatHubFeatureTests`, `ChatAccessTests`, and the
  `Microsoft.AspNetCore.SignalR.Client` test package.
- **Frontend:** `signalRService.ts` and its test (replaced by a streaming send in
  `chatApi.ts`), `ChatWindow.tsx`, `App.tsx`, `sessionErrors.ts`, the tests that mock the
  SignalR service, `vite.config.ts`, and `@microsoft/signalr` in `package.json`.
- **API:** `/chatHub` no longer exists. Any outside client must switch to
  `POST /api/chat/messages`.
- **Scale-out:** each reply is one request, so any instance can serve it and no backplane
  is needed. #20's backplane item and its "SSE versus SignalR" decision record no longer
  apply once this merges.
- **Unchanged:** `ChatService`, the AI provider, the repository and history reading. As
  today, a reply that is stopped part-way is not saved (#16).
