## Context

- `ChatWindow` keeps `conversationId` in React state. The app has no router: `App.tsx`
  switches between the sign-in, chat and billing views with state.
- `GET /api/chat/{id}/history` already exists and checks ownership. It answers 404 alike for
  a missing conversation, someone else's, and (through its `guid` route constraint) a value
  that is not an id. Its `ChatMessageDto` has `content`. The UI's `ChatMessage` has `message`.
- `chatApi.getHistory` exists but nothing calls it, and it throws one generic error for every
  failure.
- `ChatService.StreamResponseAsync` saves the reply only after the model finishes. Cancelling
  the request, or a model failure, skips the save.

## Goals / Non-Goals

**Goals:**

- A reload, a new tab or a saved link reopens the conversation with its history.
- A refresh during a reply keeps what the model had produced.
- A way to start over, since a refresh no longer does.

**Non-Goals:**

- **A conversation list or sidebar.** It needs a list endpoint, titles and new UI (#26).
- **Keeping a reply running after a refresh.** The reply is tied to the request, so a
  refresh still stops it. The intended follow-up (#27) generates replies in a background job,
  independent of the request: a refresh would then reattach to a reply still being written,
  and the UI would show it complete once it finishes. Saving the partial reply here is the
  interim step, and the background job would replace it.
- Marking a saved partial reply as incomplete. See Risks.

## Decisions

**The id lives in the query string, `?conversation=<id>`, not the path.**
A path such as `/c/<id>` needs the host to serve `index.html` for unknown paths. The Vite dev
server does, but deployment (#21) has not been designed yet. A query parameter works on any
static host. It is read and written with `URLSearchParams` and `history.replaceState`. No
router: one parameter does not justify a dependency and restructuring `App`. Only the
`conversation` parameter is set or deleted; the path, other parameters and the fragment are
kept as they are.

**`replaceState`, not `pushState`.** Each conversation is not a navigation step. Pushing would
make Back land on a URL without the id and require a `popstate` handler to reload state. With
`replaceState`, Back leaves the app as it does today.

**Only a well-formed id reaches the network.** The id is interpolated into
`/api/chat/<id>/history`, and URL parsing resolves dot segments even when escaped (`..` and
`%2e%2e` both work), so `?conversation=../../auth/sessions?x=` would make the app send an
authenticated GET to `/auth/sessions`. Escaping is not enough. `ChatWindow` accepts the value
only if it matches the canonical GUID form (8-4-4-4-12 hex digits). Anything else is treated
as not found, without a request. The `guid` route constraint stays as the server's own check.

**`ChatWindow` reads the id once on mount.** A non-empty `conversation` parameter starts a
history load. Until it settles, the window shows a loading indicator instead of the empty
chat's "What's on the agenda today?", and the input is disabled. Remounting reads it again:
App unmounts `ChatWindow` for the billing view, so returning from Manage Plan reopens the
conversation. The effect uses a cancelled flag, so
StrictMode's second mount does not apply a stale result, as the existing session-check
effect does. Other failures keep the id and keep the input disabled. Keeping it disabled
stops a message going into a conversation the user cannot see. New chat is the way out.

**`getHistory` returns a result, not a throw-for-everything.** It returns
`{ messages: ChatMessage[] }`, or `{ error }` holding one of "Conversation not found." (404),
`SESSION_EXPIRED_MESSAGE` (`SessionExpiredError` from `authorizedFetch`), or "Your
conversation could not be loaded." (anything else). This matches how `streamMessage` reports
outcomes, without exceptions. It maps `content` to `message` and narrows `role`, so
`ChatWindow` does not know the wire shape. The not-found message is a frontend constant,
since history 404s carry no body.

**The URL is written in `onConversationId`.** That is the moment the conversation's id is
known. Writing it on every event is harmless but pointless, so it is written only when the
id differs from the URL's.

**New chat is a header button, beside Manage Plan and Logout.** It is shown when there are
messages or an error, and disabled while a reply streams. Disabling avoids deciding what
New chat means to a reply in flight. It resets messages, error and `conversationId` and
removes the parameter.

**Sign-out removes the id; expiry keeps it.** `App.endSession` removes the parameter.
`abandonSession` leaves it, so signing back in mounts `ChatWindow`, which reopens the
conversation. A shared device's next user who signs in with someone else's id in the URL
sees "Conversation not found.", the same as any foreign id.

**The partial reply is saved in a `finally` in `StreamResponseAsync`.** The token loop is
wrapped in `try`/`finally`. The `finally` saves the accumulated text when it is non-empty,
passing `CancellationToken.None`: the request's token is already cancelled when the caller
has gone, and the save must still happen. A `finally` covers every way the loop can end
early: the model throws, the model observes cancellation, or the controller stops
enumerating and disposes the iterator, for example after a failed write. C# allows `await`
in a `finally` of an async iterator. A model failure before the first token produces no
text, so no reply is saved, as the spec requires.

**Tests for the save after cancellation wait for the save, not for the model.** The
existing `CallerClosesPartWay` test waits until the model sees cancellation and then reads
the repository. The save happens after that, in the `finally`, so the test must wait for the
reply to appear. Without that wait, the test races the save.

## Risks / Trade-offs

- **A partial reply looks like a whole one.** After a reload it reads like a reply that just
  ends. The model also sees it as context for the next turn. This is accepted for now; a
  completeness flag belongs with the background-generation follow-up, which changes what
  "incomplete" means anyway.
- **The partial save can fail.** If the database is down, the exception from the `finally`
  replaces the model's error or the cancellation. The controller's catch-all logs it, and
  then either writes the error event or, if the caller has already gone, fails the write.
  That failure is logged as well. The user already has the tokens on screen, so this loses
  only the saved copy and needs no extra handling.
- **The id is visible in the URL.** It is a random GUID, the history endpoint enforces
  ownership, and a foreign id answers like an unknown one. Sharing the URL with another
  user reveals nothing.
- **A reload can beat the partial save.** The server saves the partial reply after it notices
  the request closed and the model stops. If the reloaded page asks for history first, it
  shows the user's message without the reply; the next reload shows it. Ordering the two
  needs the reply to outlive the request, which is #27. The Playwright test retries the
  reload rather than asserting once.
- **A new conversation is unreachable until its first token.** The stream sends the
  conversation id with the first token, and the conversation and the user's message are
  saved before that. A reload while waiting for the first token, or a model failure before
  it, leaves that conversation saved but with no URL, so it holds only the user's message
  and the user sees an empty chat. Sending the id as soon as the message is saved would fix
  this, but would turn the 502 for a failure before the first token into an error event and
  reverse a #24 decision. It is accepted for now: the window is the model's time to first
  token, and #26's conversation list will reach such conversations too.
