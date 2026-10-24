## Context

Today `ChatHub.SendMessage` validates the message, checks the plan, then steps through
`ChatService.StreamResponseAsync` by hand. It sends `ReceiveConversationId` before the
first token and turns `ConversationNotFoundException` into "Conversation not found.".
`StreamResponseAsync` is lazy. The content check (`ChatMessage`'s constructor), the
ownership lookup and the saving of the user message all run on the first `MoveNextAsync`,
before any token. The service already does what the new endpoint needs; only the transport
around it changes.

On the client, `signalRService` keeps one shared connection. `ChatWindow` starts it on
mount, and that start renews the token, which is how a stale session reaches
`onSessionExpired` on load. `ChatWindow` sees the service only through four callbacks:
`onConversationId`, `onWord`, `onComplete` and `onError`. `authorizedFetch` already
renews a stale token, shares a renewal in flight, and retries once after a 401.

The backend is on .NET 8, which has no built-in server-sent events result.
`TypedResults.ServerSentEvents` arrives in .NET 10.

## Goals / Non-Goals

**Goals:**

- Move streaming to one HTTP request per reply while keeping every check the hub makes and
  the order they run in.
- Keep `ChatService`, the AI provider and the repository unchanged.
- Keep the callback shape `ChatWindow` depends on, so its message rendering does not
  change.

**Non-Goals:**

- Keep-alive comments during a long pause in a reply. Nothing between the browser and the
  API times out an idle stream today; revisit when a proxy is added (#20).
- Resuming a broken stream, or saving a partial reply (#16).
- A stop button. Closing the request is supported and used on unmount, but no new UI.
- Upgrading to .NET 10.

## Decisions

### An action on `ChatController`, not a minimal API endpoint

`POST /api/chat/messages` is an action on `ChatController` with
`[Authorize(Policy = "CanChat")]`, next to the history action. The API is built from
controllers, and the class-level `[Authorize]` and the policy attributes then work as they
do for history. A minimal API endpoint would be the one exception in the codebase.

### Check everything before the response starts

The action makes the checks in the hub's order, so the answers stay the same:

1. The policy: 401 when not signed in, 403 without `conversation:create`.
2. Blank content: 400 with "Message cannot be empty.".
3. Content longer than `ChatMessage.MaxContentLength`: 400 with an error that the message
   is too long.
4. The plan: 403 with "Chat is not available on your current plan.".
5. Get the enumerator and call the first `MoveNextAsync`. A `ConversationNotFoundException`
   is 404 with "Conversation not found.". Any other exception there means the model failed
   before its first token: log it and answer 502 with "The reply could not be completed.".
   An `OperationCanceledException` caused by the request aborting writes nothing.

Steps 2 and 3 check content in the action, not by catching the `ArgumentException`
thrown by `ChatMessage`. That keeps "Message cannot be empty." word for word, and keeps
other `ArgumentException`s, such as a missing user id, from turning into a 400. Errors use
`ProblemDetails` with the message in `detail`, as ASP.NET Core does by default.

The request body is a record whose `Message` is declared as `string?`, with an optional
`ConversationId`. With nullable references on, `[ApiController]` treats a non-nullable
string as required, and would refuse an empty or missing message itself with "The Message
field is required." before step 2 could run. A malformed body or an invalid id is still
refused by model validation with its own 400.

The 401 and 403 from the policy come from the authorization middleware and have no body,
as for history today. The client shows its fallback message for them.

Only after the first item does the action set the status and headers and write anything.
This is the same point at which the hub sent `ReceiveConversationId`, so a refused caller
never sees a conversation id or a token.

*Alternative:* start the stream immediately and report refusals as `event: error`. It was
rejected because every refusal would then be a 200, the `chat-access` scenarios could not
be stated as status codes, and `authorizedFetch`'s 401 retry would not see the refusal.

### Wire format: named events, JSON payloads

```
event: conversation
data: "3f2b…"

event: token
data: "Hel"

event: token
data: "lo\nworld"

event: done
data: {}
```

A failure after the stream has started sends `event: error` with
`data: {"message":"The reply could not be completed."}`.

Every `data` value is JSON. SSE splits `data` on line breaks and the model's tokens can
contain them, so a raw token would need its lines split on the server and joined again on
the client. With JSON it is one line every time. Named events keep the token stream apart
from the control events without a type field on each one.

The status is 200, with `Content-Type: text/event-stream`, `Cache-Control: no-cache` and
`X-Accel-Buffering: no`.

### A small hand-written writer instead of a library

A private helper on the controller, or a small class next to it, writes
`event:`/`data:` frames and flushes after each one. Response buffering is turned off
through `IHttpResponseBodyFeature.DisableBuffering()`. It is about 20 lines, and the wire
format above is its whole contract.

*Alternative:* a NuGet SSE package. Rejected: not worth a dependency for this, and .NET 10's
built-in result can replace the helper later.

### Failures after the stream has started

Any exception after the first event is logged and answered with the fixed `error` event.
The text never includes the exception. An `OperationCanceledException` caused by the
request aborting writes nothing: no one is listening. `StreamResponseAsync` saves the
assistant message only after the model finishes, so a failed or stopped reply is not
saved and the user message stays saved. Nothing in the service changes.

### The request's cancellation token stops the model

The action's `CancellationToken` (the request's `RequestAborted`) is passed to
`StreamResponseAsync`, which passes it to the AI provider. On the client, an
`AbortController` cancels the fetch.

### Frontend: `streamMessage` in `chatApi.ts` with a small parser

`streamMessage(message, conversationId, callbacks, signal)` goes next to `getHistory` and
sends through `authorizedFetch`. It reads `response.body` with a `TextDecoder`, splits on
blank lines and dispatches the four event names to the existing callbacks.

How each outcome is reported:

| Outcome | Reported as |
|---|---|
| `SessionExpiredError` | `SESSION_EXPIRED_MESSAGE` |
| Network failure before a response | `CONNECT_FAILED_MESSAGE` |
| An error status | the `ProblemDetails` `detail`, or "Your message could not be sent." when there is no body, as with a 403 from the policy |
| An `error` event | its message |
| The stream ends without `done` | "The reply was interrupted." |
| An abort | nothing: the component that asked is gone |

*Alternative:* `@microsoft/fetch-event-source`. Rejected: it adds a dependency while we are
removing one. Its automatic retry would also resend a POST, which repeats the user's
message.

`authorizedFetch` retries a POST once after a 401. This is safe: a 401 comes from
authentication before the action runs, so nothing was saved.

### Session check moves to an explicit renewal on mount

`ChatWindow`'s mount effect calls `authService.getValidToken()` instead of
`signalRService.start()`. A `SessionExpiredError` still shows `SESSION_EXPIRED_MESSAGE`
and calls `onSessionExpired`. Any other error on mount is ignored: there is no longer a
connection to fail, and the first send reports it. This keeps the e2e test "a session
ending by itself leaves the credential exchangeable" passing without changes.

*Alternative:* move the check to `App`. Rejected: `App` would need to know which screen
renews, and the current wiring of `onSessionExpired` through `ChatWindow` already works.

### Unmount aborts the reply in flight

`ChatWindow` holds an `AbortController` per send and aborts it on unmount. Signing out
unmounts `ChatWindow`, so this replaces `signalRService.stop()` in `App`.

### Tests

The API tests use `HttpClient` with `HttpCompletionOption.ResponseHeadersRead` and a small
SSE reader in the test project.

`ChatApiFactory` keeps `FakeAiProvider` and gains a way to script the model:

- yield a token and wait for a signal (for the tokens-arrive-separately test);
- throw after the first token (for the mid-reply failure test);
- observe cancellation (for the caller-goes-away test).

`ChatAccessTests` keeps the real `PermissionService` and changes its connection tests to
POSTs to the new endpoint.

### Docs

`docs/architecture/decisions/0002-stream-chat-replies-over-sse.md` records the choice:
SSE over SignalR because of the shape of the traffic, the token in the header, and
stateless scale-out. The README's streaming sections, diagram and tech table change to
match.

The CORS policy's `AllowCredentials()` stays. Its comment, "Required for SignalR", becomes
true again by naming the refresh cookie instead.

## Risks / Trade-offs

- [Headers wait for the model's first token, because the first `MoveNextAsync` covers both
  the lookup and the first token] → This is the same point at which the hub sent the
  conversation id. The UI's typing indicator covers the wait. A proxy with a short
  first-byte timeout would need its timeout raised (#20).
- [The model returns no tokens] → The stream sends only `done`, with no conversation id,
  because the service yields the id only alongside a token. The conversation and the
  user's message are saved, but the client cannot continue that conversation, so its next
  message starts a new one. Today's hub behaves the same way. The chat-streaming spec
  covers only replies with at least one token. Fixing it means the service handing back
  the id before the tokens, which is outside this change.
- [The model fails before its first token] → The client gets a 502 but no conversation id,
  while the conversation and the user's message are already saved. As with an empty reply,
  the next message starts a new conversation. Today's hub behaves the same way.
- [Buffering anywhere collapses the stream into one chunk] → The writer flushes each
  event, buffering is turned off, and a test checks that the first token arrives before
  the model is allowed to produce the second. The Vite dev proxy passes chunked responses
  through.
- [A long pause in a reply looks idle to a proxy] → Accepted for now; keep-alive comments
  are a non-goal.
- [Outside clients of `/chatHub` break] → None are known; the PR is marked BREAKING.

## Migration Plan

Backend and frontend ship together in one PR and one deploy. There is no stored data to
migrate. To roll back, revert the PR.
