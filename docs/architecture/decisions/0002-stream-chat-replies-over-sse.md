# Stream chat replies over server-sent events, not SignalR

- Status: accepted
- Date: 2026-10-25
- Issue: [#24](https://github.com/asifarshadullah/chatapp/issues/24)

## Context and Problem Statement

Chat replies streamed over a SignalR hub. The hub had one method, and it streamed one reply
back to the one client that asked for it. Nothing was pushed from the server unprompted, and
nothing was broadcast to other users or groups. The traffic is a request with a streamed
response, and SignalR was carrying it as a persistent two-way connection.

That cost more than a dependency on each side:

- Browsers cannot set headers on WebSocket or EventSource connections, so the access token
  travelled in the query string as `?access_token=`. There it can end up in URLs, proxy logs
  and browser history.
- Under long polling, the HTTP request that delivered a message has ended by the time the hub
  method runs. The hub had to read the caller from the connection rather than the request,
  and a test had to pin that down.
- Connections live in one process's memory, so running a second instance would need a Redis
  backplane only to route replies back to the right connection.
- The client kept a shared connection with its own start, stop, reconnect and
  StrictMode handling. A component unmounting could abort a connection another instance was
  still waiting on.

The question this settles is which transport should carry a streamed reply.

## Considered Options

- **Server-sent events over one HTTP request per reply.** `POST /api/chat/messages` takes the
  message and answers with a `text/event-stream` of the conversation id, the tokens, and an
  end marker.
- **Keep SignalR.** It works, and #20 would add the backplane and measure it.
- **SignalR forced onto its SSE transport.** This removes WebSockets but keeps the hub, the
  negotiate step, the client library and the query-string token.
- **SSE through `@microsoft/fetch-event-source`** rather than a hand-written parser.

## Decision Outcome

Chosen option: **server-sent events over one HTTP request per reply**. It fits the shape of
the traffic, and everything else follows from that:

- The token travels in the `Authorization` header, like every other request, through the
  same `authorizedFetch` that renews a stale token and retries once after a 401.
- The caller is the request's caller.
- Any instance can serve any reply.
- Refusals are ordinary HTTP errors sent before the stream starts, so permission, plan,
  content and ownership checks answer with status codes instead of errors inside a
  connection.

Keeping SignalR was rejected because none of what it adds is used, and its costs, above all
the token in the URL, are paid on every request. Forcing SignalR onto SSE was rejected because
it keeps every one of those costs and only changes the wire. `fetch-event-source` was rejected
because it is a dependency added in the same change that removes one, and its automatic retry
would resend a POST and repeat the user's message. The parser it would replace is about thirty
lines.

### Consequences

- Good, because no access token appears in a URL.
- Good, because the chat path has one transport, plain HTTP, and its tests are plain HTTP
  tests.
- Good, because scaling out needs no backplane. #20's backplane item and its planned "SSE
  versus SignalR" record no longer apply.
- Bad, because .NET 8 has no built-in SSE result, so a small writer frames and flushes events
  by hand. .NET 10's `TypedResults.ServerSentEvents` can replace it.
- Bad, because there are no keep-alive messages. A long pause in a reply looks idle to a
  proxy with a short timeout. Nothing in front of the API has one today; add keep-alive
  comments when a proxy arrives.
- Bad, because the response waits for the model's first token before it starts. That is when
  the conversation id becomes known, and when the hub used to send it too. The UI's typing
  indicator covers the wait.
- Neutral, because a broken stream is not resumed, as with SignalR, and a reply stopped
  part-way is not saved (#16).
