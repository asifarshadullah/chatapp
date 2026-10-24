## Why

The chat HTTP endpoints skip the checks the real-time hub enforces (#15). `POST /api/chat`
returns AI replies to any signed-in user, whatever their role or plan, which is both a
permission gap and a way around billing. `GET /api/chat/{id}/history` never checks the
`conversation:read` permission, so a role without it could still read. The UI sends only
through the hub, so nothing needs the HTTP send path.

## What Changes

- **BREAKING** Remove `POST /api/chat`. Sending a message is possible only over the
  real-time connection, which already requires the `conversation:create` permission and a
  plan that includes chat.
- Remove the code that only served that endpoint: the controller action, the service's
  non-streaming send method, the request and response DTOs, and the unused frontend
  `sendMessage` with its type and tests.
- Require the `conversation:read` permission for `GET /api/chat/{id}/history`. A caller
  without it gets 403 Forbidden, whatever conversation id they name. No plan check on
  reading.
- Ownership stays as it is: with the permission, another user's or an unknown conversation
  is still 404.

## Capabilities

### New Capabilities

- `chat/chat-access`: which permission each chat operation needs, and that the real-time
  connection is the only way to send a message.

### Modified Capabilities

- `chat/conversation-ownership`: its scenarios that send over HTTP go away with the
  endpoint, and the requirements that named HTTP and real-time sending side by side now
  name only the real-time connection. Its history scenarios now name a caller whose role
  grants `conversation:read`, since a caller without it gets 403 first.

## Impact

- **Backend:** `ChatController`, `IChatService` and `ChatService`, `ChatRequestDto`,
  `ChatResponseDto`, a new authorization policy in `Program.cs`, and the controller and
  service tests.
- **Frontend:** `chatApi.ts` (`sendMessage` removed), `types/chat.ts` (`ChatResponse`
  removed), `chatApi.test.ts`.
- **API:** `POST /api/chat` no longer exists. Any outside client that used it must switch
  to the hub.
- **Roles:** every seeded role holds `conversation:read` today, so no current user loses
  access.
