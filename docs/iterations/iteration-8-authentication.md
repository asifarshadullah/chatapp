# Iteration 8: Identity & Authentication

## Goal
Users can register with email/password and sign in with Google. Our API issues its own JWT
after successful authentication. All chat endpoints require authentication — anonymous access
is rejected. Application and Domain layers remain completely unaware of JWT, OAuth, or any
specific provider.

## Context
Iteration 7 delivered real LLM responses. The application currently has no concept of "who"
is chatting — all conversations are anonymous and undifferentiated. This iteration introduces
identity as a new bounded context (`Chat.Identity`) that sits alongside the existing
`Messaging` context. The two contexts share only one thing: a `Guid UserId`.

The key architectural moves:
- `AppUser` entity lives in `Chat.Identity.Domain` — completely separate from `Conversation`
- `ICurrentUser` in `Chat.Identity.Application` exposes `UserId` to any layer that needs it,
  without exposing `HttpContext` or JWT details
- All OAuth/JWT mechanics live in Infrastructure — swapping Google for Keycloak touches
  zero Application or Domain code

---

## Architecture overview

```
POST /auth/login or GET /auth/google
  └─ IIdentityService.LoginAsync / HandleExternalCallbackAsync  ← Application interface
       ├─ MongoUserStore (IUserStore<AppUser>)                  ← Infrastructure (MongoDB)
       └─ JwtTokenGenerator                                     ← Infrastructure (issues JWT)

Authenticated request (JWT in header)
  └─ JWT middleware validates token
       └─ ICurrentUser.UserId resolved from HttpContext.User claims
            └─ ChatHub / ChatController reads ICurrentUser      ← injected, no HttpContext coupling
```

**JWT claim shape (lean — no permissions):**
```json
{ "sub": "3fa85f64-...", "email": "user@example.com", "role": "User", "account_type": "Individual" }
```

Permissions and plan features are NOT in the token — they are resolved at runtime from the
database in Iteration 9. See Design Concepts doc for reasoning.

---

## New bounded context — folder structure

```
backend/src/
  Chat.Identity.Domain/
    Entities/AppUser.cs                 Id, Email, DisplayName, UserType, ExternalLogins, CreatedAt
    Enums/UserType.cs                   Individual, Organization, Enterprise
    ValueObjects/ExternalLogin.cs       Provider (string), ProviderKey (string)

  Chat.Identity.Application/
    Interfaces/
      IIdentityService.cs               RegisterAsync, LoginAsync, HandleExternalCallbackAsync
      ICurrentUser.cs                   UserId, Email, Role, IsAuthenticated
    DTOs/
      RegisterDto.cs                    Email, Password, DisplayName
      LoginDto.cs                       Email, Password
      TokenDto.cs                       AccessToken, ExpiresAt, UserId
      UserProfileDto.cs                 UserId, Email, DisplayName, UserType

  Chat.Identity.Infrastructure/
    Services/
      IdentityService.cs                Implements IIdentityService
      JwtTokenGenerator.cs              Builds and signs JWT from AppUser
      CurrentUser.cs                    Implements ICurrentUser from IHttpContextAccessor
    Stores/
      MongoUserStore.cs                 IUserStore<AppUser> backed by MongoDB users collection
    Configuration/
      JwtSettings.cs                    Secret, Issuer, Audience, ExpiryMinutes
      GoogleAuthSettings.cs             ClientId, ClientSecret

tests/
  Chat.Identity.Tests/
    Services/IdentityServiceTests.cs    Unit tests with FakeUserStore
    Integration/AuthEndpointTests.cs    Integration tests with FakeIdentityService
```

---

## Phase 1: Register with email/password

The integration test is written first. It won't compile — that compilation failure
is the signal to create the domain entities, application interfaces, and infrastructure
classes the test needs. Nothing is created before a test demands it.

### Cycle 1.1 — Register happy path
**RED** — `Chat.Identity.Tests/Integration/AuthEndpointTests.cs`
```
Register_WithValidData_ReturnsTokenWithCorrectClaims
```
Test won't compile → forces all of the following into existence.

**GREEN** — create the minimum to make it pass, in dependency order:

*Domain* (`Chat.Identity.Domain`):
```csharp
// Enums/UserType.cs
public enum UserType { Individual, Organization, Enterprise }

// Entities/AppUser.cs  — two-constructor pattern
public class AppUser
{
    public Guid Id { get; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public UserType UserType { get; private set; }
    public IReadOnlyList<ExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();
    public DateTime CreatedAt { get; }

    private readonly List<ExternalLogin> _externalLogins = new();

    // Create new
    public AppUser(string email, string displayName, UserType userType = UserType.Individual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = Guid.NewGuid();
        Email = email.ToLowerInvariant();
        DisplayName = displayName;
        UserType = userType;
        CreatedAt = DateTime.UtcNow;
    }

    // Reconstruct from storage
    public AppUser(Guid id, string email, string displayName, string passwordHash,
        UserType userType, IEnumerable<ExternalLogin> externalLogins, DateTime createdAt)
    {
        Id = id; Email = email; DisplayName = displayName; PasswordHash = passwordHash;
        UserType = userType; CreatedAt = createdAt;
        _externalLogins.AddRange(externalLogins);
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;
    public void AddExternalLogin(ExternalLogin login) => _externalLogins.Add(login);
}
```

*Application* (`Chat.Identity.Application`):
```csharp
// DTOs/RegisterDto.cs + DTOs/TokenDto.cs
public record RegisterDto(string Email, string Password, string DisplayName);
public record TokenDto(string AccessToken, DateTime ExpiresAt, Guid UserId);

// Interfaces/IIdentityService.cs  — RegisterAsync only; other methods added in later cycles
public interface IIdentityService
{
    Task<TokenDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);
}
```

*Infrastructure* (`Chat.Identity.Infrastructure`):
```csharp
// Configuration/JwtSettings.cs
public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "chatapp";
    public string Audience { get; set; } = "chatapp";
    public int ExpiryMinutes { get; set; } = 60;
}

// Services/JwtTokenGenerator.cs  — builds and signs JWT from AppUser
// Stores/MongoUserStore.cs  — IUserStore<AppUser>; implement FindByEmailAsync + CreateAsync
// Services/IdentityService.cs  — RegisterAsync: hash password, call store, generate JWT
```

*API* (`Chat.Api`):
```csharp
// Controllers/AuthController.cs  — POST /auth/register only
// Program.cs additions:
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* validate using JwtSettings */ });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IIdentityService, IdentityService>();
```

---

### Cycle 1.2 — Duplicate email is rejected
**RED** — `Chat.Identity.Tests/Services/IdentityServiceTests.cs` (unit test, FakeUserStore)
```
RegisterAsync_WithDuplicateEmail_ThrowsInvalidOperationException
```

**GREEN** — add `FindByEmailAsync` existence check at the top of `IdentityService.RegisterAsync`

---

## Phase 2: Login with email/password

### Cycle 2.1 — Login happy path
**RED** — `AuthEndpointTests.cs` (integration test)
```
Login_WithValidCredentials_ReturnsToken
```
Test won't compile → forces `LoginDto` and `LoginAsync` onto `IIdentityService`.

**GREEN**:
```csharp
// DTOs/LoginDto.cs
public record LoginDto(string Email, string Password);

// IIdentityService — add:
Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default);

// IdentityService.LoginAsync — find user by email, BCrypt.Verify, generate JWT
// AuthController — add POST /auth/login action
```

### Cycle 2.2 — Wrong password is rejected
**RED** — `IdentityServiceTests.cs` (unit test, FakeUserStore)
```
LoginAsync_WithWrongPassword_ThrowsUnauthorizedAccessException
```

**GREEN** — `BCrypt.Verify` returns false → throw `UnauthorizedAccessException`

---

## Phase 3: Protect existing endpoints

### Cycle 3.1 — Unauthenticated request returns 401
**RED** — `AuthEndpointTests.cs` (integration test)
```
Unauthenticated_SendMessage_Returns401
```
Test fails because `ChatController` and `ChatHub` currently accept anonymous requests.

**GREEN**:
- Add `[Authorize]` to `ChatController` and `ChatHub`
- This immediately breaks all 42+ existing tests (they get 401) — fix in the same GREEN step:
  update `ChatApiFactory` to issue a fake JWT so all 43 tests pass together

---

## Phase 4: Current user profile

`ICurrentUser` is introduced here, driven by the `/auth/me` endpoint test. It is not
created upfront — Application and Domain have no knowledge of HTTP or JWT until
this cycle forces the interface to exist.

### Cycle 4.1 — GET /auth/me returns profile
**RED** — `AuthEndpointTests.cs` (integration test)
```
GetMe_Authenticated_ReturnsUserProfile
```
Test won't compile → forces `ICurrentUser`, `UserProfileDto`, and `GetUserAsync` into existence.

**GREEN**:
```csharp
// DTOs/UserProfileDto.cs
public record UserProfileDto(Guid UserId, string Email, string DisplayName, string UserType);

// Interfaces/ICurrentUser.cs  — Application layer only; no HttpContext, no JWT
public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}

// IIdentityService — add:
Task<UserProfileDto?> GetUserAsync(Guid userId, CancellationToken ct = default);

// Infrastructure/Services/CurrentUser.cs  — implements ICurrentUser from IHttpContextAccessor
// AuthController — add GET /auth/me  [Authorize]
// Program.cs — add: builder.Services.AddScoped<ICurrentUser, CurrentUser>();
```

---

## Phase 5: Google OAuth

`ExternalLogin` value object is created here, not in Phase 1, because nothing
before this cycle requires it.

### Cycle 5.1 — New Google user: account is created and JWT is returned
**RED** — `IdentityServiceTests.cs` (unit test, FakeUserStore)
```
HandleExternalCallbackAsync_NewUser_CreatesUserAndReturnsToken
```
Test won't compile → forces `ExternalLogin`, `HandleExternalCallbackAsync`, and
`FindByLoginAsync` into existence.

**GREEN**:
```csharp
// Domain/ValueObjects/ExternalLogin.cs
public record ExternalLogin(string Provider, string ProviderKey);

// IIdentityService — add:
Task<TokenDto> HandleExternalCallbackAsync(string provider, string providerKey,
    string email, string displayName, CancellationToken ct = default);

// IdentityService.HandleExternalCallbackAsync — no user by providerKey → create AppUser
//   with ExternalLogin, store, generate JWT
// MongoUserStore — add FindByLoginAsync (lookup by provider + providerKey)

// Infrastructure/Configuration/GoogleAuthSettings.cs
public class GoogleAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

// Program.cs additions:
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection("Google"));
// (existing AddAuthentication chain) .AddGoogle(options => {
//     options.ClientId = builder.Configuration["Google:ClientId"]!;
//     options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
// });

// AuthController — add GET /auth/google + GET /auth/callback/google
```

### Cycle 5.2 — Existing Google user: no duplicate is created
**RED** — `IdentityServiceTests.cs` (unit test, FakeUserStore)
```
HandleExternalCallbackAsync_ExistingUser_ReturnsTokenWithoutCreating
```

**GREEN** — `FindByLoginAsync` returns existing user → skip creation, generate JWT directly

---

## appsettings additions
```json
"Jwt": {
  "Secret": "your-256-bit-secret-here",
  "Issuer": "chatapp",
  "Audience": "chatapp",
  "ExpiryMinutes": 60
},
"Google": {
  "ClientId": "your-google-client-id",
  "ClientSecret": "your-google-client-secret"
}
```

---

## Acceptance criteria
1. POST /auth/register creates user in MongoDB `users` collection, returns JWT
2. POST /auth/login with valid credentials returns JWT
3. JWT decoded shows: `sub` (Guid), `email`, `role="User"`, `account_type="Individual"`
4. GET /auth/google redirects to Google consent page
5. Google callback creates/finds user and returns our JWT (not Google's token)
6. Unauthenticated request to `/chatHub` or `/api/chat` returns 401
7. All 42+ existing tests still pass (ChatApiFactory updated with fake JWT)
8. New Identity tests pass with no real Google, no real MongoDB (FakeUserStore)

## Verification commands
```bash
dotnet test backend/ChatApp.sln --verbosity normal
cd backend/src/Chat.Api && dotnet run
# curl POST /auth/register
# curl POST /auth/login
# decode JWT at jwt.io — verify claims
```

## What you will learn
- Bounded context split in practice — Identity domain isolated from Messaging domain
- The two-constructor pattern extended to AppUser
- Value objects: ExternalLogin (immutable, no identity)
- Claims-Based Identity: lean JWT, permissions NOT in token
- OIDC as a provider abstraction — one middleware config handles all OIDC providers
- Anti-Corruption Layer: Google's claim names never appear in Application or Domain
- ICurrentUser pattern — Application decoupled from HttpContext entirely

## Decisions log
| Decision | Reason |
|---|---|
| Separate Chat.Identity.Domain project | Bounded context isolation — Identity and Messaging evolve independently |
| JWT issued by us after Google auth | We control token shape; downstream code never sees Google's token format |
| Lean JWT (no permissions) | Permissions change; JWT can't be revoked before expiry — check permissions live from DB |
| ICurrentUser interface in Application | Application needs UserId, not HttpContext — testable without HTTP stack |
| MongoDB user store (custom IUserStore) | Consistent with existing stack; one DB to operate |
| Google only first | Proves the OIDC abstraction; Microsoft + Keycloak added with near-zero extra code |
