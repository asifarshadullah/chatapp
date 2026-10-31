## Context

**Storage.** A conversation is one MongoDB document in `conversations`, with these fields:
- `_id`: a string Guid.
- `OwnerId`: a string Guid.
- `CreatedAt`.
- `Messages`: an array of embedded messages.

It has no title and no last-activity field. There are no indexes beyond `_id`.

**Saving.**
- `MongoChatRepository` (scoped) creates documents, finds them by id and owner, and pushes messages onto them. Every query already filters on `OwnerId`.
- `ChatService.StreamResponseAsync` works in this order:
  1. look up the conversation;
  2. consume the daily quota;
  3. create the conversation;
  4. save the user's message;
  5. stream from `IAiProvider`.

  A `finally` block saves whatever reply was produced. The controller sends `done` once the service's enumeration ends, so anything the service awaits after the last token delays `done`.
- `IAiProvider` (singleton, `OpenAiCompatibleProvider`) only streams. It has one `ChatClient` per offered option.

**Startup.** It runs `RoleSeeder.SeedAsync` and `MongoDailyMessageQuota.EnsureIndexesAsync`.

**Frontend.**
- `App` renders `ChatWindow` full screen.
- `ChatWindow` reads `?conversation=` once, when it mounts, and handles loading, not-found, failed and session-ended history itself. Its header is absolutely positioned and holds Settings, New chat, Manage Plan and Logout.

**Groq allowance.** Groq's free allowance for `gpt-oss-120b` is 8,000 tokens a minute, shared by every visitor, and reasoning tokens count toward it.

## Goals / Non-Goals

**Goals:**
- The list query reads only the fields it returns and is served by one index, whatever the size of the conversations.
- A title failure can never affect a reply.
- Opening a conversation from the sidebar reuses the existing URL path, so it gets the not-found, failed and session-ended handling for free.

**Non-Goals:**
- **Live updates of the list from other tabs.** The list refreshes on mount and after each send.
- **Rewriting the chat header.** It moves into the chat column but keeps its buttons, apart from New chat. Fixing #35 is out of scope.

## Decisions

### 1. Store `Title`, `LastActivityAt` and `HasReply` on the conversation document

- **Writing them:**
  - `CreateConversationAsync(userId, title)` writes the first-message title, `CreatedAt` as `LastActivityAt`, and `HasReply: false`.
  - `AddMessageAsync` adds `$max: { LastActivityAt: message.Timestamp }` to its `$push`, so the reply saved in the `finally` block also counts as activity, and saves that land out of order never move it back. For an assistant message it also sets `HasReply: true`.
- **Why `HasReply`:** a conversation is created and its message saved before the model is called. When the first reply fails before its first token, the caller never receives the id, and a retry starts a second conversation. Listing only conversations with a reply hides these, and the list then matches exactly the conversations the user was given.
- **Index:** a partial index `{ OwnerId: 1, LastActivityAt: -1, _id: -1 }` with `partialFilterExpression: { HasReply: true }`. Every list query includes `HasReply: true`, so it matches the filter and sort exactly, a page is an index range scan, and conversations without a reply take no space in the index.

*Alternative:* derive both at read time from `Messages` (`$max` of timestamps, `$first` content) in an aggregation. It needs no migration, but it cannot use an index for the sort, and it reads every message of every conversation the user has to build one page. It would also leave rename (a future issue) nowhere to write.

### 2. Keyset paging with an opaque cursor

- **The query:** `Find(OwnerId == u && HasReply && (LastActivityAt < t || (LastActivityAt == t && _id < id)))`, sorted by `LastActivityAt` descending then `_id` descending, with `Limit(limit + 1)`. The extra document says whether there is a next page.
- **The projection:** `_id`, `Title` and `LastActivityAt` only.
- **The cursor:** the last entry's `LastActivityAt` ticks and `_id`, as `"{ticks}:{id}"`, base64url-encoded.
  - A cursor that does not decode, or has non-numeric ticks or a non-Guid id, gives 400 "Invalid cursor.".
  - The owner is not in the cursor. A cursor only positions within the caller's own list, so a forged or borrowed one reveals nothing. The system does not sign cursors, since there is nothing to protect.
- **Limits:** the controller validates `limit`: the default is 20, and outside 1–50 it is 400. A non-integer `limit` fails model binding and gets the framework's 400.
- **Times:** `lastActivityAt` is serialized as ISO 8601 UTC with a `Z`. The Mongo driver stores `DateTime` as UTC, and values read back are `DateTimeKind.Utc`.
- **Response:** `GET /api/chat/conversations` returns `{ conversations: [{ id, title, lastActivityAt }], nextCursor }`, and is gated by `[Authorize(Policy = "CanReadConversation")]`.

*Alternative:* offset paging (`skip`). A conversation that moves to the top between pages shifts every later offset by one, so an entry is skipped or repeated. Keyset paging has the same race for the moved conversation itself, but only that one, and the frontend's merge by id hides the duplicate.

### 3. Title rules in the domain, generation behind `ITitleGenerator`

- **`ConversationTitle.From(text)` in Chat.Domain** holds the cut rule from conversation-titles: trim, collapse whitespace, then 60 characters with a word-boundary cut and "…". The first-message title, the generated title and the startup backfill all use it, so the three cannot drift.
  - **Code points, not UTF-16 units.** `string.Length` counts a surrogate pair as two, and a cut by index can split an emoji into an invalid half. The rule enumerates `Rune`s (`text.EnumerateRunes()`) to count and cut.
  - It does not cut by grapheme cluster. A cut can still separate an emoji's skin-tone modifier or a ZWJ sequence, which leaves valid text that looks slightly different. That is accepted.
- **`ConversationTitle.Clean(modelText)`** applies the cleaning rules from conversation-titles, then `From`:
  1. keep the first line that is not blank;
  2. remove a leading `Title:`, ignoring case;
  3. trim, and remove one layer of surrounding quotes or backticks (`"`, `'`, `“ ”`, `‘ ’`, `` ` ``);
  4. remove one trailing full stop.

  It is in the domain next to `From`, so its cases are plain unit tests.
- **`IAiProvider.CompleteAsync(modelId, instructions, content, maxTokens, ct)`** is a single non-streamed completion: a system message, one user message and no history window. `OpenAiCompatibleProvider` implements it with `CompleteChatAsync` on the same client and option, including reasoning effort. Its errors are mapped as for streaming: 429 becomes `ModelRateLimitedException`, and the key is redacted.
- **`ITitleGenerator.GenerateAsync(modelId, firstMessage, ct)`** is defined in Chat.Application and implemented as `ModelTitleGenerator` in Chat.Infrastructure. It does the following:
  - sends the instruction "Write a title of at most six words for a conversation that begins with the user's message. Reply with the title only.";
  - sends the first message, cut to 1,000 characters to bound the tokens;
  - caps the reply at `Ai:MaxTitleTokens` (default 300, validated as positive at startup). This leaves `gpt-oss` room to reason at `low` effort. A model that spends it all reasoning returns no text, and the first-message title stays;
  - cleans the answer with `ConversationTitle.Clean`;
  - returns `null` when nothing is left.
- **Failures are contained in the generator.** It enforces a 10-second timeout with its own `CancellationTokenSource`, and catches and logs every failure: a warning for a rate limit, an error otherwise. It returns `null` instead of throwing, so `ChatService` handles only a failure to store the title.

*Alternative:* put the title method on `IAiProvider` itself. That puts the prompt and the cleanup rule in the transport class. Keeping them in a generator also lets the service tests fake titles without faking the provider.

### 4. Generate after the reply and before `done`, sequentially

`ChatService.StreamResponseAsync`:
- creates the conversation with `ConversationTitle.From(content)`;
- after the provider's stream completes normally with some text, and only for a conversation it created, calls `ITitleGenerator.GenerateAsync(modelId, content, CancellationToken.None)`;
- on a non-null result, calls `SetTitleAsync(conversationId, userId, title)`, logging and swallowing any failure so the complete reply still ends with `done`.

The call is placed after the `await foreach` and inside the `try`, so it runs neither when the provider throws nor when the caller cancels during the reply. The reply is saved in the `finally` block, independently. The quota was consumed once, earlier, and the generator never touches it.

**A caller who leaves during title generation.** The generator and `SetTitleAsync` both take `CancellationToken.None`, so the title is still stored, bounded by the generator's own 10-second timeout. The service is still being enumerated by the controller at that point. The controller's write of `done` then fails as cancelled, which is already handled.

**Only the first reply.** "First reply" is the reply to the message that created the conversation in this call: the service knows this because `existing` is null. No flag is stored. A conversation whose first reply fails part-way, and every conversation saved before this change, keeps its first-message title. This caps title requests at one per conversation.

**The cost.** The end marker of a new conversation's first reply is delayed by one short model call. On Groq that is well under a second; with local Ollama it is a few seconds. The input stays blocked until `done`.

*Alternatives considered:*
- **Fire and forget after `done`.** The sidebar refreshes on `done` and would usually show the first-message title, with the real one appearing only on some later refresh. A detached task also outlives the request scope that the scoped repository belongs to.
- **Generate concurrently with the reply.** On local Ollama, the two requests compete for one model, which would delay the reply's first token, and the first token's latency matters more.
- **A `title` stream event after `done`.** This breaks the stream contract that `done` is last, and the frontend stops reading at `done`.

### 5. Startup backfill, idempotent

`MongoChatRepository.EnsureIndexesAsync(db)` runs at startup next to the quota's own setup. It does two things:
1. It creates the index.
2. It brings old documents up to date. For each document where `Title`, `LastActivityAt` or `HasReply` is missing, it sets the following:
   - **`Title`:** `ConversationTitle.From` of the first message, or "New conversation" when there are no messages. Such a conversation has no reply, so it is never listed.
   - **`LastActivityAt`:** the latest message's timestamp, or `CreatedAt`.
   - **`HasReply`:** whether any message has the role `Assistant`.

It reads only those documents, in C#, so the cut rule is the domain's. Documents without `OwnerId` are skipped; no list or lookup can reach them anyway.

*Alternative:* an update pipeline, which runs entirely on the server. It is faster, but the word-boundary cut would have to be written again in aggregation operators. At demo scale the C# loop costs nothing.

### 6. Frontend: a shell owns the sidebar, and `ChatWindow` is remounted per conversation

- **The layout.** A new `ChatShell` replaces `ChatWindow` in `App`. It renders a two-column layout:
  - **The sidebar:** an MUI `Drawer`, `permanent` at `md` (900 px) and up, `temporary` below that, opened from a menu `IconButton` shown only below `md`.
  - **The chat column:** `ChatWindow` with `key={openCount}`.
- **Opening a conversation from the sidebar:** `showConversationInUrl(id)`, then `openCount += 1`. `ChatWindow` remounts, reads the URL and loads the history through its existing path.
- **New chat:** `removeConversationFromUrl()` and a remount. `ChatWindow` loses its own New chat button and `startNewChat`. The sidebar always shows the button, disabled while the chat is empty with no error, or while a reply streams. `ChatWindow` reports whether it is empty through `onCanStartNewChatChange(boolean)`.
- **Choosing the open conversation** is a no-op: the shell compares the id with the current one and does not remount.
- **What `ChatWindow` reports to the shell:**
  - `onConversationChange(id | undefined)`, used to mark the current entry;
  - `onStreamingChange(boolean)`, used to disable the entries and New chat;
  - `onSendFinished()`, which bumps a `refreshKey` the sidebar watches to reload the first page.
- **`ConversationSidebar`** owns the list: it loads the first page on mount and whenever `refreshKey` changes, and renders the list, its loading, empty and failed states, "Show more" and New chat, with titles as text children (never `dangerouslySetInnerHTML`). The shell owns only what the sidebar is told: the current id, whether a reply streams, and whether New chat is available.
- **`listConversations(cursor?)` in `chatApi`** returns `{ conversations, nextCursor } | { error }`. It maps a session error to `SESSION_EXPIRED_MESSAGE` like `getHistory`, and the sidebar then calls `onSessionExpired`.
- **Merging after a send:** the reloaded first page goes first, followed by the earlier entries whose ids are not in it, and the existing `nextCursor` is kept.
- **Failures:**
  - **First load:** the list is in the failed state with Retry.
  - **Reload after a send:** the result is ignored and the entries stay. A session-expired result still calls `onSessionExpired`.
  - **"Show more":** the entries stay, "More conversations could not be loaded." shows under them, and the button stays so it can retry.
- **Signing out:** the shell unmounts, so its list goes with it. The next user's shell starts empty and loads their own list; nothing is cached across users.

*Alternative:* keep one `ChatWindow` and teach it to switch conversations in place. Its history effect, `requested` state and refs all assume one conversation per mount. A remount costs a repeat `GET /models`, and it guarantees that no state from the previous conversation leaks into the next.

## Risks / Trade-offs

- **[A title call fails for the shared Groq allowance at busy times]** → It is logged as a warning, and the first-message title stays. It costs about 100–400 tokens per new conversation, against 8,000 a minute.
- **[A model title is odd or carries text from a prompt injection in the first message]** → It is shown only to the conversation's owner, as plain text, and is cut to 60 characters. The first message was the owner's own.
- **[The first reply's `done` arrives later]** → The delay is bounded by the 10-second timeout, and in practice it is under a second on Groq. It happens only once per conversation.
- **[Conversations without a reply stay stored but unlisted]** → They hold one user message each. They are as unreachable as they were before this change, since nobody received their id. Deleting them is left to the delete issue.
- **[A conversation moves up while the user is paging]** → Merging by id prevents duplicates. The moved entry also appears at the top after the next refresh.
- **[The backfill runs on every start]** → It filters on missing fields, so after the first run it updates nothing. Its only cost is a collection scan of `conversations`, which is small.
- **[The remount re-fetches the models on every switch]** → It is one small GET, and caching it in the shell can come later if it matters.

## Migration Plan

1. Deploy as usual. On the first start, the backfill titles every existing conversation and creates the index, with no manual step.
2. **Rollback:** redeploying the previous image ignores `Title` and `LastActivityAt`, and the index is harmless. Nothing needs undoing.
3. `Ai:MaxTitleTokens` has a default in code, like the other bounds, so Render needs no new setting.

## Open Questions

- What wording the title instruction should use, and whether 300 tokens is enough for `gpt-oss-120b` at `low` effort. Both are tuned against the live model during apply without changing any spec. If titles come back empty, the cap goes up.
