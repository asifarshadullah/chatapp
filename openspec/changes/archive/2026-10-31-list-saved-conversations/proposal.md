## Why

A saved conversation can be reopened only from its URL (#26). Someone who did not keep the link, or who wants last week's conversation back, cannot find it, although every conversation is stored with its owner. The UI never lists them, and the backend has no endpoint that could.

## What Changes

- **A list endpoint.** `GET /api/chat/conversations` returns the caller's own conversations, most recently active first, a page at a time. Each entry has its id, title and the time of its latest message. It is gated by `conversation:read` like history, does not depend on the plan, and never returns another user's conversation, whatever the caller's role.
- **Only conversations with a reply are listed.** A conversation whose first reply failed before its first token was never named to the user, and a retry starts a new one. Listing it would show a duplicate that the chat doesn't know about.
- **Every conversation gets a title.**
  - When a conversation starts, its title is the first message, cut to 60 characters. This title is stored immediately.
  - After the first reply has streamed, the model that answered is asked for a short title, which replaces the stored one before the stream's end marker.
  - If generating the title fails, times out or comes back empty, the first-message title stays and the reply is unaffected.
  - The title call does not count toward the daily message limit, and it never appears in the conversation.
- **Each conversation records its last activity**, which is the time of its latest message, and whether it has a reply. The list is sorted by last activity, served by an index on owner and last activity.
- **Existing conversations are brought up to date at startup.** Any conversation missing a title gets its first message as one, and any missing a last-activity time gets its latest message's time. Conversations that have no owner stay unreachable.
- **A sidebar in the chat.**
  - It lists the conversations by title, newest first, with a "Show more" button when there are more.
  - On wide screens it is always shown. On narrow screens it is behind a menu button and opens as a drawer.
  - Choosing an entry opens that conversation the same way its URL does.
  - The open conversation is highlighted.
  - The list refreshes when a reply ends, so a new conversation appears at the top and a continued one moves up.
  - New chat moves into the sidebar. It is always shown there, and is disabled on an empty chat and during a reply.

## Non-goals

- **Renaming and deleting conversations.** These need separate issues; the stored title is what renaming would change.
- **Searching conversations.**
- **#35, the header buttons overlapping the first message.** That bug keeps its own issue.

## Capabilities

### New Capabilities
- `chat/conversation-list`: the list endpoint, covering its order, paging, what each entry carries, and how existing conversations are brought up to date.
- `chat/conversation-titles`: covers how a conversation's title is set from its first message, replaced by a generated one, and kept when generation fails.
- `chat/conversation-sidebar`: the sidebar, covering how it shows, pages, opens and refreshes the list.

### Modified Capabilities
- `chat/chat-access`: listing conversations requires `conversation:read` and does not depend on the plan.
- `chat/conversation-ownership`: the list holds only the caller's own conversations, for every role.
- `chat/conversation-links`: New chat is always shown in the sidebar, and is disabled instead of hidden on an empty chat.

## Impact

- **Backend:**
  - **Chat.Domain:** `Conversation` gains a `Title` and the rule that cuts a message to a title.
  - **Chat.Application:**
    - `IChatRepository` gains listing and setting the title.
    - `IAiProvider` gains a single, non-streamed completion.
    - `ChatService` sets the first-message title, generates the real one after the first reply, and lists conversations.
  - **Chat.Infrastructure:**
    - `ConversationDocument` gains `Title`, `LastActivityAt` and `HasReply`.
    - `MongoChatRepository` lists conversations by keyset paging, and its startup step creates the index and brings old documents up to date.
    - `OpenAiCompatibleProvider` implements the completion.
  - **Chat.Api:**
    - `ChatController` gains `GET /api/chat/conversations`.
    - `Ai:MaxTitleTokens` is a new setting, validated at startup.
- **Data:** the `conversations` collection gets three new fields and a new partial index `{ OwnerId: 1, LastActivityAt: -1, _id: -1 }` over conversations with a reply. Existing documents are updated in place at startup, and the update is idempotent.
- **Model use:** one extra, small request per new conversation, against Groq's shared free allowance of 8,000 tokens a minute.
- **Frontend:**
  - A `ConversationSidebar` component and `listConversations` in `chatApi`.
  - The chat becomes a sidebar plus the chat column.
  - Choosing a conversation remounts `ChatWindow` on the new URL, so it reuses the existing loading and not-found handling.
  - New chat moves out of the chat header.
- **E2E:** a Playwright test that starts a conversation, finds it in the sidebar after New chat, and reopens it.
