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
- [x] Manual E2E: real LLM response streams in browser ✅

### Post-iteration fix: System prompt ✅
- [x] `SystemPrompt` field added to `OllamaSettings` with sensible default
- [x] `OllamaAiProvider` prepends system message (role: system) before conversation history
- [x] `appsettings.Development.json` overrides prompt — tunable without code changes

---

## Completed iterations
- **Iteration 1:** Backend skeleton + echo endpoint — 16 tests, tagged `iteration-1` (commit 73145d1)
- **Iteration 2:** React UI (MUI) — 22 component/service tests, `npm run build` clean
- **Iteration 3:** Playwright E2E — 5 tests, Chromium, page object pattern
- **Iteration 4:** In-memory conversation history — 38 tests (domain + repo + service + controller)
- **Iteration 5:** MongoDB persistence — 45 tests total; 7 MongoDB integration tests against real Docker DB
- **Iteration 6:** SignalR streaming + UX polish — 4 hub integration tests + 10 frontend component tests
- **Iteration 7:** Local LLM (Ollama) — IAiProvider abstraction, OllamaAiProvider, StreamResponseAsync, system prompt — 42 backend tests
- **Iteration 8:** Identity & Authentication — JWT register/login, Google OAuth, ICurrentUser, bounded-context split (Chat.Identity.*), 25 backend tests (13 existing + 12 new)

---

## Iteration 8: Identity & Authentication ✅

### Phase 1: Register (vertical slice) ✅
- [x] Cycle 1.1 RED: `Register_WithValidData_ReturnsTokenDto` integration test
- [x] Cycle 1.1 GREEN: Chat.Identity.Domain (AppUser, ExternalLogin, UserType), Chat.Identity.Application (IUserStore, ITokenGenerator, IIdentityService, DTOs), Chat.Identity.Infrastructure (JwtTokenGenerator, IdentityService, MongoUserStore, CurrentUser), AuthController POST /auth/register, Program.cs DI wiring
- [x] Cycle 1.2: `RegisterAsync_WithDuplicateEmail_ThrowsInvalidOperationException` unit test + guard

### Phase 2: Login ✅
- [x] Cycle 2.1: `LoginAsync_WithValidCredentials_ReturnsToken` + `Login_WithValidCredentials_ReturnsTokenDto`
- [x] Cycle 2.2: `LoginAsync_WithWrongPassword_ThrowsUnauthorizedAccessException` + `LoginAsync_WithUnknownEmail_ThrowsUnauthorizedAccessException`

### Phase 3: Protect endpoints ✅
- [x] Cycle 3.1 RED: `Unauthenticated_SendMessage_Returns401`
- [x] Cycle 3.1 GREEN: `[Authorize]` on ChatController + ChatHub; `TestAuthHandler` in ChatApiFactory keeps 13/13 existing tests green

### Phase 4: ICurrentUser + GET /auth/me ✅
- [x] Cycle 4.1: `GetMe_Authenticated_ReturnsUserProfile` + `GetMe_Unauthenticated_Returns401` → `ICurrentUser`, `CurrentUser` (via IHttpContextAccessor), GET /auth/me on AuthController, `AuthApiFactory.CreateAuthenticatedClient()`

### Phase 5: Google OAuth ✅
- [x] Cycle 5.1: `HandleExternalCallbackAsync_NewUser_CreatesUserAndReturnsToken`
- [x] Cycle 5.2: `HandleExternalCallbackAsync_ExistingUser_ReturnsTokenWithoutCreating`
- [x] GET /auth/google + GET /auth/callback/google on AuthController
- [x] ExternalCookie scheme + AddGoogle in Program.cs

### Verification ✅
- [x] 13/13 Chat.Api.Tests pass (existing tests unbroken)
- [x] 12/12 Chat.Identity.Tests pass (5 integration + 7 unit)
- [x] Build: 0 errors, 0 warnings

---

---

## Iteration 9: Frontend Authentication + Backend Authorization ✅

### Frontend Track ✅

#### FA1: authService TDD ✅
- [x] FA1.1 `login_withValidCredentials_storesTokenAndReturnsIt` → `authService.login()`, localStorage
- [x] FA1.2 `register_storesTokenAndReturnsIt` → `authService.register()`
- [x] FA1.3 `logout_clearsToken` → `authService.logout()`
- [x] FA1.4 `isAuthenticated_whenTokenExists_returnsTrue` → `authService.isAuthenticated()`

#### FA2: LoginPage component TDD ✅
- [x] FA2.1 `renders_emailPasswordInputsAndSubmitButton` → `LoginPage.tsx` MUI form
- [x] FA2.2 `submit_login_callsAuthService_andCallsOnLogin`
- [x] FA2.3 `submit_register_callsAuthServiceRegister`
- [x] FA2.4 `whenAuthFails_showsErrorMessage` → `<Alert>` on catch

#### FA3: App conditional routing TDD ✅
- [x] FA3.1 `whenUnauthenticated_showsLoginPage` → App.tsx with `isAuthenticated` state
- [x] FA3.2 `whenAuthenticated_showsChatWindow`
- [x] FA3.3 `afterSuccessfulLogin_showsChatWindow` → `onLogin` callback sets state

#### FA4: Token propagation ✅
- [x] `signalRService.ts` — `accessTokenFactory: () => authService.getToken()` at negotiate time
- [x] `chatApi.ts` — Authorization Bearer header on all REST requests

### Backend Track ✅

#### Phase 1: IPermissionService unit tests ✅
- [x] 1.1 `IsAuthorizedAsync_AdminRole_ReturnsTrue_ForAnyPermission` → IPermissionService, IRoleStore, RoleInfo, PermissionService (wildcard `"*"`)
- [x] 1.2 `IsAuthorizedAsync_UserRole_ReturnsTrue_ForGrantedPermission`
- [x] 1.3 `IsAuthorizedAsync_UserRole_ReturnsFalse_ForUnlistedPermission`
- [x] 1.4 `IsAuthorizedAsync_UnknownRole_ReturnsFalse`

#### Phase 2: MongoRoleStore + RoleSeeder ✅
- [x] `RoleDocument.cs` — BSON model with `ToRoleInfo()` mapper
- [x] `MongoRoleStore.cs` — queries `roles` collection
- [x] `RoleSeeder.cs` — seeds User/OrgAdmin/Admin on empty collection
- [x] `PermissionService` — 5-min TTL cache via IMemoryCache
- [x] `Program.cs` — registers IRoleStore, IPermissionService, handler, seeder call

#### Phase 3: Policy wiring integration tests ✅
- [x] `PermissionRequirement` + `PermissionRequirementHandler` — delegates to IPermissionService
- [x] Named policies: CanChat, CanShareConversation, CanInviteUsers, AdminOnly
- [x] `ChatHub` — `[Authorize(Policy = "CanChat")]`
- [x] `AuthController` — `GET /auth/admin-probe [Authorize(Policy = "AdminOnly")]`
- [x] `AuthApiFactory` — `CreateTokenWithRole()`, seeded `FakeRoleStore`
- [x] `AuthorizationEndpointTests` — AdminOnly_WithUserRole_Returns403, AdminOnly_WithAdminRole_Returns200, AdminOnly_WithoutToken_Returns401
- [x] `ChatApiFactory` — `AlwaysAllowPermissionService` keeps 13 existing tests green

#### Phase 4: Feature gating ✅
- [x] `Chat.Billing.Application` — `IPlanFeatureService` interface
- [x] `Chat.Billing.Infrastructure` — `StubPlanFeatureService` (always enables all)
- [x] `ChatHub` — feature guard before streaming (`throw HubException` if disabled)
- [x] `ChatHubFeatureTests` — Cycle 4.1 (disabled→HubException), Cycle 4.2 (enabled→streams)

### Verification ✅
- [x] 76/76 backend tests pass (29 Application + 15 Api + 13 Infrastructure + 19 Identity)
- [x] 43/43 frontend tests pass
- [x] Build: 0 errors, 0 warnings

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
| 2026-03-18 | SystemPrompt in OllamaSettings (Infrastructure) | Controls model behaviour; tunable via appsettings without recompile; not a business rule so stays in Infrastructure |
| 2026-03-18 | System message prepended before conversation history | Models respect standing instructions when given as first message with role=system |
| 2026-03-23 | Chat.Identity.* bounded context separate from Chat.* | Identity and messaging share only Guid UserId; prevents model contamination across contexts |
| 2026-03-23 | MapInboundClaims = false in JWT validation | Keeps `sub` claim as `"sub"` throughout pipeline; consistent with JwtTokenGenerator and CurrentUser |
| 2026-03-23 | TestAuthHandler in ChatApiFactory for existing tests | Auto-authenticates all requests after [Authorize] added; 13 existing tests stay green without issuing real JWTs |
| 2026-03-23 | AuthApiFactory with real test JWT secret | New identity tests use real JWT validation path; CreateToken() helper issues verifiable tokens |
| 2026-03-23 | ExternalCookie scheme as SignInScheme for Google OAuth | Completes OAuth round-trip before AuthController reads principal; avoids default scheme conflict with JWT |
| 2026-03-23 | ICurrentUser interface in Application layer | Hides IHttpContextAccessor from domain/app; Infrastructure provides CurrentUser backed by HttpContext |
| 2026-03-23 | RoleInfo record in Application layer, RoleDocument in Infrastructure | Keeps Application free of BSON/MongoDB types; RoleDocument.ToRoleInfo() maps at the boundary |
| 2026-03-23 | PermissionService reads ICurrentUser.Role (from JWT) — no extra DB call | Role already in token; avoids per-request DB round-trip to look up user's role |
| 2026-03-23 | 5-min TTL cache in PermissionService | Role lookups are read-heavy and rarely change; cache cuts DB load without staleness risk |
| 2026-03-23 | AlwaysAllowPermissionService in ChatApiFactory | Keeps 13 existing hub/controller tests green after [Authorize(Policy="CanChat")] replaced bare [Authorize] |
| 2026-03-23 | IPlanFeatureService in Chat.Billing.Application (separate context) | Feature gating belongs to billing, not identity or chat; context boundary enforced by project dependency |
| 2026-03-23 | StubPlanFeatureService enables all features | No billing logic yet; stub ships always-on behaviour until real billing plans are built |
| 2026-03-23 | authService stores JWT in localStorage | Simplest persistence across page reloads; `accessTokenFactory` reads from it at SignalR negotiate time |
| 2026-03-23 | State-based routing in App.tsx (no react-router) | App has only two views (login / chat); react-router would be over-engineering for this scope |
