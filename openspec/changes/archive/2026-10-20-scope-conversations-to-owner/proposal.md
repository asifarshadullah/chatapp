## Why

A conversation has no owner, so being signed in is the only check on it. Any
signed-in user who holds another user's conversation id can read its full
history, append messages to it, and receive AI replies generated with the
owner's history as context (#10). Conversation ids travel in URLs and SignalR
messages, so they are not secrets, and listing, deleting or sharing
conversations cannot be built safely until ownership exists.

## What Changes

- Every conversation records the user who created it, and only that user can
  read it or add messages to it. This applies to every role, Admin included.
- A conversation that belongs to someone else is answered exactly like one that
  does not exist, so the response never confirms that an id is real:
  - `GET /api/chat/{id}/history` returns 404.
  - `POST /api/chat` with a `conversationId` returns 404. Today an unknown id
    on this endpoint fails with a 500, and that is fixed here too.
  - The `SendMessage` hub method fails with "Conversation not found." Today it
    fails with SignalR's generic error.
- A request naming a conversation the caller cannot use fails. It never falls
  back to starting a new conversation.
- Conversations stored before this change have no owner and become unreachable
  to everyone. There is no migration; existing data is development data only.
- The unused `GetMessagesAsync` repository method is removed rather than
  secured.
- The real-time hub identifies the caller from its authenticated connection
  rather than from the current HTTP request. When the connection falls back
  to long polling, the request is gone by the time the hub runs, so the caller
  came back empty. That would leave new conversations without an owner, and it
  already makes the hub's plan check run for an empty user id, which passes
  only because a user with no subscription falls into the free tier. Both now
  use the real caller on every transport.

## Non-goals

- Sharing a conversation with other users. The `conversation:share`
  permission exists but stays unused; sharing will add an access rule on top
  of ownership.
- Admin or support access to other users' conversations.
- Listing or deleting a user's conversations.
- The missing permission and plan check on `POST /api/chat`, and the unchecked
  `conversation:read` permission on history, both tracked in #15.
- Frontend changes. The UI keeps the conversation id in memory only, so a real
  user cannot reach someone else's conversation without tampering.

## Capabilities

### New Capabilities

- `chat/conversation-ownership`: who may read and continue a conversation, and
  how the system answers a caller who may not.

### Modified Capabilities

None. No chat specs exist yet.

## Impact

- **Bounded context:** Chat. Identity is touched only at the composition root,
  where `Chat.Api` reads the caller's `UserId` from `ICurrentUser` and passes
  it into the Chat context as a plain `Guid`, which is all the contexts share.
- **Domain:** `Conversation` gains an owner.
- **Application:** `IChatRepository` and `IChatService` methods take the
  caller's user id; `GetMessagesAsync` is removed.
- **Infrastructure:** `MongoChatRepository` and `InMemoryChatRepository`
  filter on owner; `ConversationDocument` stores it.
- **API:** `ChatController` and `ChatHub` pass the user id down and translate
  "not found" into 404 and a hub error. `ChatHub` reads the caller from its
  connection and no longer depends on `ICurrentUser`.
- **Billing:** no code change, but the hub's plan check now receives the real
  caller's id under long polling instead of an empty one.
- **Data:** existing documents in the `conversations` collection become
  unreachable. The local development collection is cleared by hand after the
  change lands.
- **Tests:** the API test factory needs a way to sign in as a second user, and as a user
  with a chosen role.
