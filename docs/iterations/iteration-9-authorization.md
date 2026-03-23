# Iteration 9: Frontend Authentication + Backend Authorization

## Goal
1. **Frontend authentication** — login/register UI, JWT token storage, pass token to SignalR and REST calls. Fixes "Failed to connect to chat server" (401 on hub negotiate).
2. **Backend RBAC** — policy-based authorization backed by MongoDB `roles` collection. Dynamic: changing permissions = DB update, no redeploy.
3. **Feature gating stub** — `IPlanFeatureService` returns true for all features (Iteration 10 replaces with real billing).

---

## Context
Iteration 8 established *who* the user is (authentication). This iteration establishes *what* they can do (authorization) and gives users a way to authenticate from the browser.

**Two orthogonal backend systems (kept strictly separate):**
- **RBAC:** role (User/OrgAdmin/Admin) → permission strings (`conversation:create`, etc.) stored in MongoDB
- **Feature Gating:** plan tier → feature availability via `IPlanFeatureService` (stub here, real in Iteration 10)

---

## Architecture

```
Browser
  └─ LoginPage → authService → POST /auth/login → JWT stored in localStorage
  └─ ChatWindow → signalRService (accessTokenFactory reads JWT) → /chatHub [Authorize(Policy="CanChat")]
                                                                      └─ PermissionRequirementHandler
                                                                           └─ IPermissionService
                                                                                └─ IRoleStore (MongoDB roles collection, 5-min cache)
```

**Permission model:**
```
Role (stored in MongoDB roles collection)
  └─ has many permission strings  e.g. "conversation:create", "conversation:share"
  └─ Admin role has wildcard "*" (grants all permissions)

Plan tier (IPlanFeatureService stub → always true until Iter 10)
  └─ unlocks Feature enum values  e.g. Feature.Chat, Feature.DocumentUpload
```

---

## New structure

```
frontend/chat-ui/src/
  services/
    authService.ts            login/register/logout/getToken/isAuthenticated
  components/
    LoginPage.tsx             email+password form with Login/Register tab toggle

Chat.Identity.Application/
  Interfaces/
    IPermissionService.cs     IsAuthorizedAsync(Guid userId, string permission) → bool
    IRoleStore.cs             GetByNameAsync(string name) → RoleDocument?

Chat.Identity.Infrastructure/
  Data/
    RoleDocument.cs           Name (string, BsonId), Permissions (List<string>)
    RoleSeeder.cs             Seeds default User/OrgAdmin/Admin docs if collection empty
  Stores/
    MongoRoleStore.cs         Implements IRoleStore against roles collection
  Services/
    PermissionService.cs      Reads ICurrentUser.Role → looks up role → checks permission + wildcard + 5-min cache
  Authorization/
    PermissionRequirement.cs      IAuthorizationRequirement with Permission string
    PermissionRequirementHandler.cs  AuthorizationHandler — calls IPermissionService

Chat.Billing.Application/            (stub — full implementation in Iteration 10)
  Enums/
    Feature.cs                Chat, DocumentUpload, SharedConversations, CustomModels
  Interfaces/
    IPlanFeatureService.cs    IsFeatureEnabled(Guid userId, Feature feature) → bool

Chat.Billing.Infrastructure/
  Services/
    StubPlanFeatureService.cs Returns true for all features
```

**Default role seed documents:**
```json
{ "name": "User",     "permissions": ["conversation:create", "conversation:read"] }
{ "name": "OrgAdmin", "permissions": ["conversation:create", "conversation:read", "conversation:share", "user:invite"] }
{ "name": "Admin",    "permissions": ["*"] }
```

---

## Frontend Track — Authentication UI

### Phase 1: authService (TDD)

**Test file:** `frontend/chat-ui/src/services/__tests__/authService.test.ts`

- **Cycle FA1.1:** RED `login_withValidCredentials_storesTokenAndReturnsIt`
  → GREEN: `authService.ts` — `login()` POSTs `/auth/login`, stores token in `localStorage`, returns `TokenDto`

- **Cycle FA1.2:** RED `register_storesTokenAndReturnsIt`
  → GREEN: `register()` POSTs `/auth/register`, same storage

- **Cycle FA1.3:** RED `logout_clearsToken`
  → GREEN: `logout()` clears `localStorage`

- **Cycle FA1.4:** RED `isAuthenticated_whenTokenExists_returnsTrue`
  → GREEN: `isAuthenticated()` returns `!!getToken()`

### Phase 2: LoginPage component (TDD)

**Test file:** `frontend/chat-ui/src/components/__tests__/LoginPage.test.tsx`

- **Cycle FA2.1:** RED `renders_emailPasswordInputsAndSubmitButton`
  → GREEN: `LoginPage.tsx` with MUI form, email/password fields, Login/Register tab toggle

- **Cycle FA2.2:** RED `submit_login_callsAuthService_andCallsOnLogin`
  → GREEN: `handleSubmit` calls `authService.login`, then `props.onLogin()`

- **Cycle FA2.3:** RED `submit_register_callsAuthServiceRegister`
  → GREEN: register tab calls `authService.register`, then `props.onLogin()`

- **Cycle FA2.4:** RED `whenAuthFails_showsErrorMessage`
  → GREEN: try/catch sets error state → `<Alert>` rendered

### Phase 3: App conditional routing (TDD)

**Test file:** `frontend/chat-ui/src/components/__tests__/App.test.tsx`

- **Cycle FA3.1:** RED `whenUnauthenticated_showsLoginPage`
  → GREEN: `App.tsx` checks `authService.isAuthenticated()`, renders `<LoginPage>` when false

- **Cycle FA3.2:** RED `whenAuthenticated_showsChatWindow`
  → GREEN: renders `<ChatWindow>` when authenticated

- **Cycle FA3.3:** RED `afterSuccessfulLogin_showsChatWindow`
  → GREEN: `onLogin` callback calls `setIsAuthenticated(true)` transitioning to chat view

### Phase 4: Token propagation

Small code changes; covered by FA3 tests + manual e2e verification.

- `signalRService.ts` — add `accessTokenFactory: () => authService.getToken() ?? ''` to `.withUrl()`. Token read at negotiate time (not at connection build time), so always picks up the current `localStorage` value.
- `chatApi.ts` — add `Authorization: Bearer ${token}` header to all requests. Update existing test to expect the header.

---

## Backend Track — RBAC + Feature Gating

### Phase 1: IPermissionService (TDD)

**Test file:** `backend/tests/Chat.Identity.Tests/Services/PermissionServiceTests.cs`
**Test double:** `FakeRoleStore : IRoleStore` (nested, in-memory — same pattern as `FakeUserStore`)
**Design:** `PermissionService` reads `ICurrentUser.Role` (already in JWT claim) to look up role doc. No extra DB call. `userId` param kept for future audit use.

- **Cycle 1.1:** RED `IsAuthorizedAsync_AdminRole_ReturnsTrue_ForAnyPermission`
  → GREEN: `IPermissionService`, `IRoleStore`, `RoleDocument`, `PermissionService` (wildcard `"*"`)

- **Cycle 1.2:** RED `IsAuthorizedAsync_UserRole_ReturnsTrue_ForGrantedPermission`
  → GREEN: look up role doc, check `Contains(permission)`

- **Cycle 1.3:** RED `IsAuthorizedAsync_UserRole_ReturnsFalse_ForUnlistedPermission`
  → GREEN: return false when not in list and no wildcard

- **Cycle 1.4:** RED `IsAuthorizedAsync_UnknownRole_ReturnsFalse`
  → GREEN: null-guard on role document lookup

### Phase 2: MongoRoleStore + seeder

Thin infrastructure — same pattern as `MongoUserStore`. Integration test in Phase 3 is the test vehicle.
- `MongoRoleStore.cs` — implements `IRoleStore`, queries `roles` collection
- `RoleSeeder.cs` — inserts default docs if collection empty, called at startup
- `Program.cs` — register `IRoleStore → MongoRoleStore`, `AddMemoryCache()`, call seeder
- Add 5-min TTL cache to `PermissionService` (internal optimization, no dedicated test)

### Phase 3: Policy wiring (TDD — integration tests drive handler + policies)

**Test file:** `backend/tests/Chat.Identity.Tests/Integration/AuthorizationEndpointTests.cs`
**Factory:** `AuthApiFactory` — add `CreateTokenWithRole(Guid userId, string role)` helper; replace `IRoleStore` with seeded `FakeRoleStore` (real `PermissionService` runs, no MongoDB needed).
**ChatApiFactory fix (not a cycle):** Add `AlwaysAllowPermissionService` so 13 existing tests stay green after `[Authorize(Policy="CanChat")]` replaces bare `[Authorize]` on `ChatHub`.

- **Cycle 3.1:** RED `CanChat_UserRoleWithPermission_HubConnectionSucceeds`
  → GREEN: `PermissionRequirement`, `PermissionRequirementHandler`, named policies in `Program.cs`, `[Authorize(Policy="CanChat")]` on `ChatHub`
  ```
  Policies registered:
    "CanChat"              → conversation:create
    "CanShareConversation" → conversation:share
    "CanInviteUsers"       → user:invite
    "AdminOnly"            → *
  ```

- **Cycle 3.2:** RED `CanChat_UserRoleWithoutPermission_Returns403`
  → GREEN: `context.Fail()` in handler when permission not granted

- **Cycle 3.3:** RED `AdminOnly_WithUserRole_Returns403`
  → GREEN: `GET /auth/admin-probe [Authorize(Policy="AdminOnly")]` added to `AuthController`

- **Cycle 3.4:** RED `AdminOnly_WithAdminRole_Returns200`
  → GREEN: wildcard logic from Phase 1 grants access

### Phase 4: Feature gating (TDD)

**Test file:** `backend/tests/Chat.Api.Tests/Hubs/ChatHubFeatureTests.cs`

- **Cycle 4.1:** RED `SendMessage_WhenChatFeatureDisabled_ThrowsHubException`
  → GREEN: `Feature` enum, `IPlanFeatureService`, `StubPlanFeatureService`, `ChatHub` injects `IPlanFeatureService` + `ICurrentUser`, guards `Feature.Chat` before processing

- **Cycle 4.2:** RED `SendMessage_WhenChatFeatureEnabled_StreamsResponse`
  → GREEN: passes from 4.1 (stub returns true); confirms happy path

---

## Test double strategy

| Context | IPermissionService | IRoleStore | IPlanFeatureService |
|---|---|---|---|
| `PermissionServiceTests` | Real `PermissionService` | `FakeRoleStore` (in-file) | — |
| `AuthApiFactory` | Real `PermissionService` | `FakeRoleStore` (seeded) | — |
| `ChatApiFactory` | `AlwaysAllowPermissionService` | — | `StubPlanFeatureService` |
| `ChatHubFeatureTests` (4.1) | `AlwaysAllowPermissionService` | — | `AlwaysDisablePlanFeatureService` |

---

## Acceptance criteria
1. Browser shows login/register form when unauthenticated
2. After login, chat UI loads and SignalR connects successfully
3. User role: can chat, cannot share or admin
4. OrgAdmin role: can share conversations
5. Admin role: can do everything (wildcard)
6. Changing User role permissions in `roles` collection takes effect within 5 minutes (cache TTL)
7. `Feature.Chat` disabled → HubException before any AI call
8. All existing tests still pass (13 API + 12 identity)

## Verification commands
```bash
dotnet test backend/ChatApp.sln --verbosity normal
cd frontend/chat-ui && npm test
# Manual: login in browser → chat works end-to-end
# Manual: remove conversation:create from User role in MongoDB → 403 within 5 min
```

## Decisions log
| Decision | Reason |
|---|---|
| Frontend: state-based routing in App.tsx (no react-router) | App has only 2 views; no library needed for this |
| Frontend: JWT stored in localStorage | Simple; sufficient for this learning project |
| accessTokenFactory reads localStorage at negotiate time | Token always current even if connection object was cached pre-login |
| PermissionService reads ICurrentUser.Role (not userId→DB) | Role already in JWT claim; avoids extra DB lookup and wrong-layer coupling |
| Policy-based over [Authorize(Roles="X")] | Roles stored in DB; changing permissions = DB update, no redeploy |
| Permissions as strings ("conversation:create") | Human-readable, extensible without enum changes |
| Wildcard "*" for Admin | Simple; avoids listing every permission for super-admin |
| 5-minute cache TTL for permissions | Avoid per-request DB hit; small lag on permission changes acceptable |
| IPlanFeatureService stub in Iter 9 | Billing context not implemented yet; stub keeps Iter 9 testable |
| Feature enum in Billing.Application | Features are a billing/product concern, not identity concern |
