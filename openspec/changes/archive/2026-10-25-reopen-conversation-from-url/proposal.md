## Why

Refreshing the page loses the conversation you were in (#16). `ChatWindow` holds the
conversation id only in React state, so a reload, a new tab or a closed tab starts over, and
nothing in the UI can open a saved conversation again. A refresh during a reply also cancels
it, and the part already shown is thrown away. Conversations now have owners (#10), so a
conversation can be safely addressed by its id.

## What Changes

- The open conversation's id goes in the URL as `?conversation=<id>` once the reply stream
  names it. Reloading, bookmarking or opening that URL in a new tab reopens the conversation
  with its saved history, and further messages continue it.
- If the URL names a conversation the user cannot open (unknown, someone else's, or not a
  valid id), the chat shows "Conversation not found.", removes the id from the URL and starts
  empty. A value that is not a valid id is never sent to the server, so a crafted link
  cannot steer the app's authenticated requests to another path.
- A **New chat** button starts an empty conversation and removes the id from the URL. Before
  this change, a refresh was the only way to start over, and it no longer is.
- Signing out removes the id from the URL. A session that ends by itself keeps it, so
  signing back in returns to the same conversation.
- A reply that stops part-way is now saved with the tokens produced so far, whether the
  caller went away or the model failed. A refresh during a reply then shows the partial
  reply, not just the user's message. Two known limits are in the design: a reload can
  arrive before that save, and a brand-new conversation has no URL until its first token.

## Capabilities

### New Capabilities

- `chat/conversation-links`: how the open conversation is reflected in the URL and reopened
  from it: loading history, the not-found case, starting a new chat and signing out.

### Modified Capabilities

- `chat/chat-streaming`: a reply that fails part-way, or is stopped because the caller went
  away, is saved with the tokens produced so far instead of being discarded.

## Impact

- **Frontend:** `ChatWindow.tsx` (read and write the URL, load history, New chat button),
  `chatApi.ts` (`getHistory` tells not found apart from other failures and maps records to
  `ChatMessage`), `App.tsx` (remove the id on sign-out), and their tests. Playwright gains
  refresh and not-found scenarios.
- **Backend:** `ChatService.StreamResponseAsync` saves the partial reply when the stream
  ends early. No API changes: `GET /api/chat/{id}/history` already exists and checks
  ownership.
- **No new dependencies:** no router. The URL is updated with `history.replaceState`.
- **Out of scope:** a conversation list, and keeping a reply running after a refresh. These
  are #26 and #27.
