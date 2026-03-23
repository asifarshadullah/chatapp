# Iteration 9: Authorization & Role Management

## Goal
Endpoints and features are protected by role. Feature availability is gated by subscription
plan. Roles and their permissions are stored in MongoDB — adding a new role or changing what
a role can do requires a database update, not a code change or redeployment.

## Context
Iteration 8 established who the user is (authentication). This iteration establishes what the
user can do (authorization). Two orthogonal systems are introduced:

1. **RBAC (Role-Based Access Control):** A user's role (User, OrgAdmin, Admin) determines
   which operations they can perform (create, share, delete). Stored in DB — dynamic.

2. **Feature Gating (Plan-Based):** A user's subscription plan (Free, Pro, Enterprise)
   determines which product features are available to them. Checked at runtime via
   IPlanFeatureService. The actual plan and subscription entities belong to Iteration 10
   (Billing) — this iteration introduces the interface and a stub implementation.

These two concerns are kept strictly separate. A Pro plan user with a "User" role can access
Pro features but cannot perform admin operations. Do not conflate them.

---

## Architecture overview

```
Incoming request (JWT validated)
  └─ [Authorize(Policy = "CanShareConversation")]
       └─ PermissionRequirementHandler
            └─ IPermissionService.IsAuthorizedAsync(userId, "conversation:share")
                 └─ PermissionService → roles collection (MongoDB) + in-memory cache

ChatHub.SendMessage
  └─ IPlanFeatureService.IsFeatureEnabled(userId, Feature.Chat)
       └─ checks users active plan tier (stub in Iter 9, real in Iter 10)
```

**Permission model:**
```
Role (stored in MongoDB)
  └─ has many permission strings  e.g. "conversation:create", "conversation:share"

Plan tier (checked via IPlanFeatureService)
  └─ unlocks Feature enum values  e.g. Feature.Chat, Feature.DocumentUpload
```

---

## New structure

```
Chat.Identity.Application/
  Interfaces/
    IPermissionService.cs         IsAuthorizedAsync(Guid userId, string permission) → bool

Chat.Identity.Infrastructure/
  Services/
    PermissionService.cs          Queries roles collection + MemoryCache (5-min TTL)
  Data/
    RoleDocument.cs               Name (string), Permissions (List<string>)
  Authorization/
    PermissionRequirement.cs      IAuthorizationRequirement with Permission string
    PermissionRequirementHandler.cs  IAuthorizationHandler — calls IPermissionService

Chat.Billing.Application/            (stub — full implementation in Iteration 10)
  Interfaces/
    IPlanFeatureService.cs        IsFeatureEnabled(Guid userId, Feature feature) → bool
  Enums/
    Feature.cs                    Chat, DocumentUpload, SharedConversations, CustomModels

Chat.Billing.Infrastructure/
  Services/
    StubPlanFeatureService.cs     Returns true for all features (replaced in Iter 10)
```

---

## Phase 1: Permission model in MongoDB

### Task 1.1: RoleDocument
**File:** `backend/src/Chat.Identity.Infrastructure/Data/RoleDocument.cs`
```csharp
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Chat.Identity.Infrastructure.Data;

internal class RoleDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Name { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();
}
```

### Task 1.2: Seed default roles (run once on startup)
```json
// roles collection — default seed documents
{ "name": "User",     "permissions": ["conversation:create", "conversation:read"] }
{ "name": "OrgAdmin", "permissions": ["conversation:create", "conversation:read", "conversation:share", "user:invite"] }
{ "name": "Admin",    "permissions": ["*"] }
```

Adding a new role: insert a document. Granting a new permission: push to the array.
Zero code change. Zero redeployment.

---

## Phase 2: IPermissionService (TDD)

**File:** `backend/src/Chat.Identity.Application/Interfaces/IPermissionService.cs`
```csharp
namespace Chat.Identity.Application.Interfaces;

public interface IPermissionService
{
    /// <summary>
    /// Returns true if the user's role grants the given permission string.
    /// Wildcard "*" on Admin role grants all permissions.
    /// </summary>
    Task<bool> IsAuthorizedAsync(Guid userId, string permission, CancellationToken ct = default);
}
```

**Test cycles:**
**File:** `backend/tests/Chat.Identity.Tests/Services/PermissionServiceTests.cs`

- **Cycle 2.1:** RED `IsAuthorizedAsync_AdminRole_ReturnsTrue_ForAnyPermission`
  → GREEN: Admin role has "*" wildcard — return true for all

- **Cycle 2.2:** RED `IsAuthorizedAsync_UserRole_ReturnsTrue_ForConversationCreate`
  → GREEN: look up role from DB/cache, check permission list

- **Cycle 2.3:** RED `IsAuthorizedAsync_UserRole_ReturnsFalse_ForConversationShare`
  → GREEN: "conversation:share" not in User role permissions list

- **Cycle 2.4:** RED `IsAuthorizedAsync_UsesCache_DoesNotHitDbOnSecondCall`
  → GREEN: MemoryCache with 5-minute TTL — second call returns cached result

- **Cycle 2.5:** RED `IsAuthorizedAsync_CacheInvalidated_ReflectsUpdatedPermissions`
  → GREEN: force cache expiry → fresh DB read picks up the updated role document

---

## Phase 3: Policy-based Authorization wiring

### Task 3.1: PermissionRequirement + Handler
**File:** `backend/src/Chat.Identity.Infrastructure/Authorization/PermissionRequirementHandler.cs`

```csharp
using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Chat.Identity.Infrastructure.Authorization;

public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public class PermissionRequirementHandler(IPermissionService permissionService, ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!currentUser.IsAuthenticated)
        {
            context.Fail();
            return;
        }

        var allowed = await permissionService.IsAuthorizedAsync(currentUser.UserId, requirement.Permission);
        if (allowed)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
```

### Task 3.2: Register policies in Program.cs
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanChat",              p => p.AddRequirements(new PermissionRequirement("conversation:create")));
    options.AddPolicy("CanShareConversation", p => p.AddRequirements(new PermissionRequirement("conversation:share")));
    options.AddPolicy("CanInviteUsers",       p => p.AddRequirements(new PermissionRequirement("user:invite")));
    options.AddPolicy("AdminOnly",            p => p.AddRequirements(new PermissionRequirement("*")));
});

builder.Services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
```

### Task 3.3: Apply policies to endpoints
```csharp
// ChatHub — all chat requires CanChat permission
[Authorize(Policy = "CanChat")]
public class ChatHub : Hub { ... }

// Future sharing endpoint
[Authorize(Policy = "CanShareConversation")]
public async Task<IActionResult> ShareConversation(...) { ... }
```

---

## Phase 4: Feature Gating — IPlanFeatureService

### Task 4.1: Feature enum + interface (Billing.Application)
```csharp
public enum Feature
{
    Chat,                 // basic messaging — all plans
    DocumentUpload,       // Pro + Enterprise
    SharedConversations,  // Pro + Enterprise
    CustomModels,         // Enterprise only
}

public interface IPlanFeatureService
{
    /// <summary>Returns true if the user's current plan includes the given feature.</summary>
    Task<bool> IsFeatureEnabled(Guid userId, Feature feature, CancellationToken ct = default);
}
```

### Task 4.2: StubPlanFeatureService (replaced in Iteration 10)
```csharp
/// <summary>Stub returns true for all features until Billing is implemented.</summary>
public class StubPlanFeatureService : IPlanFeatureService
{
    public Task<bool> IsFeatureEnabled(Guid userId, Feature feature, CancellationToken ct = default)
        => Task.FromResult(true);
}
```

### Task 4.3: Use in ChatService / ChatHub
```csharp
// In ChatHub.SendMessage (or ChatService) — guard before processing
if (!await _planFeatureService.IsFeatureEnabled(currentUser.UserId, Feature.Chat, ct))
    throw new HubException("Your current plan does not include chat access.");
```

**TDD cycles for feature gating:**
- **Cycle 4.1:** RED `SendMessage_WhenChatFeatureDisabled_ThrowsHubException`
  → GREEN: inject IPlanFeatureService, check Feature.Chat before processing

- **Cycle 4.2:** RED `SendMessage_WhenChatFeatureEnabled_Proceeds`
  → GREEN: passes from Cycle 4.1 impl (stub returns true)

---

## Phase 5: API integration tests update

- **Cycle 5.1:** RED `AdminEndpoint_WithUserRole_Returns403`
  → GREEN: [Authorize(Policy = "AdminOnly")] endpoint + test with User role JWT

- **Cycle 5.2:** Verify all 42+ existing tests pass (permissions pass through for existing test users)

---

## Acceptance criteria
1. User role: can create conversations, cannot share or admin
2. OrgAdmin role: can share conversations
3. Admin role: can do everything
4. Changing User role permissions in `roles` collection takes effect within 5 minutes (cache TTL)
5. Adding a brand-new role in DB (no code change) — endpoint protected by that role works immediately after cache expiry
6. Feature.Chat disabled → 403/HubException before any AI call is made
7. All 42+ existing tests still pass

## Verification commands
```bash
dotnet test backend/ChatApp.sln --verbosity normal
# Manual: change User role to remove "conversation:create" in MongoDB
# Send message as User → should get 403 within 5 minutes
# Change back → works again
```

## What you will learn
- Policy-based authorization vs attribute-role authorization — when each is right
- `IAuthorizationRequirement` + `IAuthorizationHandler` — how ASP.NET Core wires them
- RBAC vs Feature Gating — two orthogonal concerns, never conflated
- In-memory caching (IMemoryCache) for DB-backed permissions — the TTL trade-off
- Wildcard permissions pattern ("*" for Admin)
- Stub implementations — IPlanFeatureService returns true until Iteration 10 replaces it

## Decisions log
| Decision | Reason |
|---|---|
| Policy-based over [Authorize(Roles = "X")] | Roles stored in DB; changing permissions = DB update, no redeploy |
| Permissions as strings ("conversation:create") | Human-readable, extensible, easy to add new permissions without enum changes |
| Wildcard "*" for Admin | Simple pattern; avoids explicitly listing every permission for the super-admin role |
| 5-minute cache TTL for permissions | Avoid per-request DB hit; small lag on permission changes is acceptable |
| IPlanFeatureService stub in Iter 9 | Billing context not implemented yet; stub keeps Iter 9 testable and unblocked |
| Feature enum in Billing.Application | Features are a billing/product concern, not identity concern — right bounded context |
