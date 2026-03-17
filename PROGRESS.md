# ChatApp Progress Tracker

## Current iteration: 5 — MongoDB Persistence ✅

### Phase 1: Project Setup ✅
- [x] Scaffold `frontend/chat-ui` with `react-swc-ts` template
- [x] Install Vitest + React Testing Library + jsdom
- [x] Configure Vitest in `vite.config.ts` (globals, jsdom, setupFiles)
- [x] Create `src/test/setup.ts` (jest-dom + scrollIntoView mock)
- [x] Configure API proxy → `http://localhost:5064`
- [x] Add `"test": "vitest"` script to package.json
- [x] Verify: `npm run build` passes with zero TypeScript errors

### Phase 2: TypeScript Types ✅
- [x] `src/types/chat.ts` — ChatMessage, ChatRequest, ChatResponse

### Phase 3: API Client TDD ✅ (vertical slices)
- [x] Cycle 3.1: RED test POST body → GREEN minimal sendMessage
- [x] Cycle 3.2: RED test success response → GREEN response.json()
- [x] Cycle 3.3: RED test HTTP error → GREEN if (!response.ok) throw
- [x] Cycle 3.4: RED test network failure → GREEN propagates naturally
- [x] Refactor: API_BASE const, types verified

### Phase 4: Components TDD ✅ (vertical slices)
- [x] MessageBubble — 3 cycles (renders content, user styling, assistant styling)
- [x] MessageList — 3 cycles (empty state, renders in order, scrolls to bottom)
- [x] ChatInput — 7 cycles (renders UI, send on click, send on Enter, no send on Shift+Enter, clears after send, disabled when empty, disabled when loading)
- [x] ChatWindow — 5 cycles (renders both, adds user msg, API + response, loading state, error)
- [x] Verify: 22 tests passing

### Phase 5: Styling ✅
- [x] `src/components/Chat.css` — full-height layout, bubbles, input bar, loading dots, error banner
- [x] Replace `App.tsx` boilerplate — renders only `<ChatWindow />`
- [x] Replace `index.css` with minimal reset
- [x] Verify: `npm run build` zero errors, `npm test` 22/22 passing

---

## Iteration 3: Playwright E2E Tests ✅

### Phase 1: Setup ✅
- [x] Scaffold `e2e/playwright/` with `@playwright/test` 1.58.2
- [x] Install Chromium browser binary
- [x] Configure `playwright.config.ts` — baseURL, webServer auto-start, Chromium only, screenshot on failure

### Phase 2: E2E Tests ✅
- [x] Test 2.1: Send message and receive echo response
- [x] Test 2.2: Multiple messages in sequence (3 user + 3 assistant)
- [x] Test 2.3: Send button disabled when input is empty
- [x] Test 2.4: Loading state during API call (route delay + disabled assertion)
- [x] Test 2.5: Page elements are present

### Phase 3: Helper ✅
- [x] `tests/helpers/chat-page.ts` — ChatPage page object (sendMessage, getUserMessages, getAssistantMessages, getSendButton, getInput, waitForAssistantResponse)
- [x] Verify: `npx playwright test` — 5/5 passing in < 5s

---

## Completed iterations
- **Iteration 1:** Backend skeleton + echo endpoint — 16 tests, tagged `iteration-1` (commit 73145d1)
- **Iteration 2:** React UI (MUI) — 22 component/service tests, `npm run build` clean
- **Iteration 3:** Playwright E2E — 5 tests, Chromium, page object pattern
- **Iteration 4:** In-memory conversation history — 38 tests (domain + repo + service + controller)
- **Iteration 5:** MongoDB persistence — 45 tests total; 7 MongoDB integration tests against real Docker DB

---

## Iteration 5: MongoDB Persistence ✅

### Phase 1: Docker Compose Setup ✅
- [x] `docker-compose.yml` — MongoDB 7 on port 27018, Mongo Express on 8081
- [x] `appsettings.Development.json` — MongoDB connection string
- [x] `Chat.Infrastructure/Configuration/MongoDbSettings.cs`

### Phase 2: MongoDB Repository TDD ✅
- [x] Add MongoDB.Driver 2.30.0 to Chat.Infrastructure
- [x] Cycle 2.2: RED `CreateConversationAsync_StoresConversationInDatabase` → GREEN scaffold MongoChatRepository + ConversationDocument + insert
- [x] Cycle 2.3: RED `GetConversationAsync_WithValidId_ReturnsStoredConversation` → GREEN find by id
- [x] Cycle 2.4: RED `GetConversationAsync_WithInvalidId_ReturnsNull` → GREEN return null on miss
- [x] Cycle 2.5: RED `AddMessageAsync_PersistsMessageToDatabase` → GREEN push message document
- [x] Cycle 2.6: RED `GetMessagesAsync_ReturnsAllStoredMessages` → GREEN return mapped messages
- [x] Cycle 2.7: RED `GetMessagesAsync_ReturnsMessagesInChronologicalOrder` → GREEN sort by timestamp
- [x] Bonus: `Conversation_PersistsAcrossRepositoryInstances` — passes without new impl
- [x] Added reconstruction constructors to Conversation and ChatMessage domain entities

### Phase 3: Swap DI ✅
- [x] `ChatApiFactory` — custom WebApplicationFactory replacing MongoDB with InMemory for API tests
- [x] `ChatControllerTests` updated to use `ChatApiFactory` (no Docker needed)
- [x] `Program.cs` updated — MongoDB DI wired; `MongoChatRepository` registered

### Phase 4: Verification ✅
- [x] 23/23 Application tests pass
- [x] 13/13 Infrastructure tests pass (6 InMemory + 7 MongoDB)
- [x] 9/9 API integration tests pass (all use InMemory via ChatApiFactory)

---

## Current iteration: 6 — SignalR Streaming + UX Polish ✅

### Phase 1: SignalR Backend Setup ✅
- [x] Task 1.1: `AddSignalR()` in Program.cs (included in ASP.NET Core 8 — no extra NuGet)
- [x] Task 1.2: Register hub in Program.cs — `app.MapHub<ChatHub>("/chatHub")`
- [x] Task 1.3: CORS updated to add `AllowCredentials()` (required for SignalR)
- [x] Task 1.3: Added `Microsoft.AspNetCore.SignalR.Client` to `Chat.Api.Tests.csproj`
- [x] Cycle 1.4: RED `SendMessage_StreamsEchoResponseWordByWord` → GREEN `ChatHub` with `IAsyncEnumerable<string>`, yield words with 50ms delay
- [x] Cycle 1.5: RED `SendMessage_StoresMessagesInConversation` → GREEN inject `IChatService`, emit `ReceiveConversationId` before stream
- [x] Cycle 1.6: RED `SendMessage_WithExistingConversationId_ContinuesSameConversation` → GREEN (passes from Cycle 1.5 impl)
- [x] Cycle 1.7: RED `SendMessage_WithEmptyContent_ThrowsHubException` → GREEN validate + throw `HubException`

### Phase 2: Frontend SignalR Integration ✅
- [x] Task 2.1: Add `/chatHub` WebSocket proxy to `vite.config.ts`
- [x] `signalRService.ts` created — `HubConnectionBuilder`, `StreamCallbacks` interface, word-by-word streaming
- [x] Cycle 2.2: RED `renders streaming words as they arrive` → GREEN ChatWindow accumulates words via `streamingIdRef`
- [x] Cycle 2.3: RED `shows typing indicator while streaming` → GREEN `isStreaming` state + `aria-label="typing indicator"` box
- [x] Cycle 2.4: RED `hides typing indicator when streaming completes` → GREEN clear `isStreaming` on `onComplete`
- [x] Cycle 2.5: RED `shows error banner when connection fails` → GREEN `onError` callback clears streaming message
- [x] Existing ChatWindow tests updated to mock `signalRService` instead of `chatApi`

### Phase 3: UX Polish ✅
- [x] Typing indicator: CircularProgress + "Assistant is typing…" text while `isStreaming=true`
- [x] Message fade-in animation (`bubble-in` keyframe in `Chat.css`)
- [x] Input disabled while streaming (`isLoading={isStreaming}` prop)
- [x] Error banner clears incomplete streaming message on failure

### Phase 4: E2E Verification ✅
- [x] Cycle 4.1: RED `response streams word by word with typing indicator` — written before Phase 3
- [x] Updated existing loading state E2E test to use streaming (removed route interception, checks indicator)
- [x] Existing response-text E2E tests pass unchanged (Playwright normalizes trailing whitespace)

---

## Current iteration: 7 — Local LLM Integration (Ollama) ✅

### Phase 1: IAiProvider interface ✅
- [x] `Chat.Application/Interfaces/IAiProvider.cs`

### Phase 2: Update ChatService (TDD) ✅
- [x] Add `StreamResponseAsync` to `IChatService`
- [x] Update `ChatService` constructor to accept `IAiProvider`
- [x] Cycle 2.2: `StreamResponseAsync_YieldsTokensFromAiProvider`
- [x] Cycle 2.3: `StreamResponseAsync_ConversationIdPresentOnEveryItem`
- [x] Cycle 2.4: `StreamResponseAsync_SavesUserMessageBeforeAnyTokenYielded`
- [x] Cycle 2.5: `StreamResponseAsync_SavesCompleteAssistantMessageAfterStreamCompletes`
- [x] Cycle 2.6: `StreamResponseAsync_PassesFullHistoryIncludingUserMessageToProvider`
- [x] Cycle 2.7: `StreamResponseAsync_WithNoConversationId_CreatesNewConversation`

### Phase 3: Update ChatHub (TDD) ✅
- [x] Cycle 3.1: `SendMessage_StreamsAiTokens` → GREEN (hub calls StreamResponseAsync)
- [x] Cycle 3.2: `SendMessage_StoresMessagesInConversation` (asserts full "Fake AI response")
- [x] Cycle 3.3: `SendMessage_WithExistingConversationId_ContinuesSameConversation`
- [x] Cycle 3.4: `SendMessage_WithEmptyContent_ThrowsHubException`
- [x] Controller test updated: echo → AI response

### Phase 4: OllamaAiProvider (Infrastructure) ✅
- [x] OllamaSharp 4.0.22 added to Chat.Infrastructure
- [x] `OllamaSettings.cs` config class
- [x] Ollama section added to appsettings.Development.json (BaseUrl + Model)
- [x] `OllamaAiProvider` implemented

### Phase 5: DI wiring + test isolation ✅
- [x] `Program.cs` registers OllamaSettings + OllamaAiProvider
- [x] `ChatApiFactory` replaces both IChatRepository and IAiProvider with fakes
- [x] `FakeAiProvider` added to ChatApiFactory (yields "Fake AI response")

### Phase 6: Verification ✅
- [x] 29/29 Application tests pass
- [x] 13/13 API tests pass (no Ollama/Docker needed)
- [ ] Manual E2E: real LLM response streams in the browser (run manually)

---

## Completed iterations
- **Iteration 1:** Backend skeleton + echo endpoint — 16 tests, tagged `iteration-1` (commit 73145d1)
- **Iteration 2:** React UI (MUI) — 22 component/service tests, `npm run build` clean
- **Iteration 3:** Playwright E2E — 5 tests, Chromium, page object pattern
- **Iteration 4:** In-memory conversation history — 38 tests (domain + repo + service + controller)
- **Iteration 5:** MongoDB persistence — 45 tests total; 7 MongoDB integration tests against real Docker DB
- **Iteration 6:** SignalR streaming + UX polish — 4 hub integration tests + 10 frontend component tests
- **Iteration 7:** Local LLM (Ollama) — IAiProvider abstraction, OllamaAiProvider, StreamResponseAsync — 42 backend tests

---

## Decisions log
| Date | Decision | Reason |
|------|----------|--------|
| 2025-02-07 | Target .NET 8 (SDK 8.0.417) | User preference; pinned via global.json |
| 2025-02-07 | Echo-only, no AI/LLM integration | Focus on architecture and patterns first |
| 2025-02-07 | Controllers, not Minimal API | User preference for learning controller patterns |
| 2025-02-07 | Vitest + RTL for frontend tests | Component tests in Iteration 2 alongside UI |
| 2026-02-27 | Import defineConfig from vitest/config | Fixes TS error when tsconfig.node.json restricts types to ["node"] |
| 2026-02-27 | Add vitest/globals to tsconfig.app.json types | Allows vi global to be used in test files without explicit import |
| 2026-02-27 | Mock scrollIntoView in setup.ts | jsdom doesn't implement scrollIntoView; needed for MessageList tests |
| 2026-03-06 | Docker MongoDB on port 27018 | Port 27017 conflicts with local MongoDB installation |
| 2026-03-06 | authMechanism=SCRAM-SHA-256 in connection string | MongoDB 7 doesn't serve SCRAM-SHA-1 by default; .NET driver 2.30.0 requires explicit mechanism |
| 2026-03-06 | ChatApiFactory overrides IChatRepository with InMemory | API integration tests stay Docker-free; only Infrastructure tests need Docker |
| 2026-03-06 | Reconstruction constructors on Conversation and ChatMessage | Enables mapping from MongoDB documents back to domain entities with preserved Ids and timestamps |
| 2026-03-07 | SignalR hub uses IAsyncEnumerable<string> for server streaming | Native .NET 8 streaming; no custom protocol needed |
| 2026-03-07 | ReceiveConversationId sent via Clients.Caller.SendAsync before stream | Decouples conversationId delivery from word stream; client receives it before first word |
| 2026-03-07 | streamingIdRef + functional setState for in-place message updates | Avoids closure over stale state; streaming message updated in the messages array directly |
| 2026-03-07 | signalRService mocked via vi.mock factory in component tests | Avoids real WebSocket connections in jsdom; StreamCallbacks interface keeps mock API clean |
| 2026-03-07 | 50ms delay per word in ChatHub | Visually perceptible streaming effect; keeps hub tests under 1s per test |
| 2026-03-18 | IAiProvider in Application, OllamaAiProvider in Infrastructure | Application defines what it needs; Infrastructure fulfills it — dependency inversion in practice |
| 2026-03-18 | StreamResponseAsync returns IAsyncEnumerable<(Guid, string)> | ConversationId must reach the hub before first token; tuple carries it on every yield |
| 2026-03-18 | OllamaSharp 4.0.22 over raw HttpClient | Handles NDJSON streaming protocol; keeps OllamaAiProvider focused on mapping, not HTTP parsing |
| 2026-03-18 | FakeAiProvider in ChatApiFactory | All API/hub integration tests run offline with no Ollama dependency |
| 2026-03-18 | Model name in appsettings.Development.json | Switching models (gemma2:2b → phi3:mini) requires zero code changes |
