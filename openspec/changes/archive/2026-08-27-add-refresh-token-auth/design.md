## Context

See proposal.md — Why. The constraints that shape the approach:

- `ITokenGenerator.Generate(AppUser)` is synchronous and returns
  `TokenDto(AccessToken, ExpiresAt, UserId)`. Refresh tokens need persistence, so generation
  and storage cannot both sit behind that synchronous signature.
- `IUserStore` has no notion of tokens, and `AppUser` is a small aggregate. Refresh tokens
  have their own lifecycle — they are consumed, rotated, and revoked independently of the
  user — so they do not belong inside the user aggregate.
- `AppUser` establishes the project's two-constructor pattern: one constructor for creation,
  one for reconstruction from storage that bypasses guards. New entities follow it. The
  archive-conversation retrospective in `docs/architecture/` records why: an invariant
  written for mutation can otherwise block the reconstruction path.
- The frontend reaches the API through Vite's proxy on the same origin in development
  (`/auth`, `/api`, `/chatHub` → `localhost:5064`), so a cookie set by the API is same-origin
  from the browser's perspective. The API is served over plain HTTP locally.
- `signalRService.accessTokenFactory` is synchronous today and throws `SessionExpiredError`
  when no token is available.

## Goals / Non-Goals

**Goals:**

- Keep the refresh credential unreadable by JavaScript, so the long-lived credential is not
  reachable through XSS.
- Make a stolen refresh token detectable rather than silently useful.
- Keep the Identity context self-contained: no new type crosses into Chat or Billing.
- Keep the existing `TokenDto` response shape working for the current frontend.

**Non-Goals:**

- Distributed revocation or a token-introspection endpoint. A single API instance reading its
  own MongoDB collection is sufficient at this size.
- Encrypting refresh tokens at rest. Hashing is enough for the threat being addressed (see
  Decisions).
- Cross-origin deployment. The design assumes API and frontend share an origin, as they do
  behind the Vite proxy.

## Decisions

### `RefreshToken` is its own entity in `Chat.Identity.Domain`, not part of `AppUser`

It has independent lifecycle and cardinality — many live tokens per user, each consumed or
revoked on its own schedule. Folding it into `AppUser` would force loading and saving the
whole user aggregate on every refresh, and would make the user document grow without bound.

It belongs in **Domain** because consumption, expiry, and revocation are rules, not
mechanics: `IsUsable(now)`, `Consume()`, `Revoke()` express invariants with no I/O. It
carries `Id`, `UserId`, `TokenHash`, `FamilyId`, `ExpiresAt`, `ConsumedAt`, `RevokedAt`, and
follows the two-constructor pattern.

*Alternative considered:* a value object embedded in `AppUser`. Rejected for the aggregate
growth and the read-modify-write contention on the user document.

### The family is an identifier on the token, not a separate aggregate

Every token issued from one authentication shares a `FamilyId`; the first token generates it,
and each successor inherits it. Revoking a family is one predicate update over the
collection.

*Alternative considered:* a `TokenFamily` entity holding its members. Rejected — it adds an
aggregate whose only state is "revoked or not", which a flag on the members already carries.

### `IRefreshTokenStore` is defined in `Chat.Identity.Application`, implemented in `Chat.Identity.Infrastructure`

Following the inward dependency rule, exactly as `IUserStore` and `ITokenGenerator` already
do. The Application layer states what it needs — find by token hash, add, update, revoke a
family — and `MongoRefreshTokenStore` supplies it. Application must not know that tokens live
in MongoDB, so `RefreshTokenDocument` mirrors the `UserDocument`/`FromDomain`/`ToDomain`
pattern and stays in Infrastructure.

### Only a hash of the token is stored

The raw token goes to the client; the database holds a SHA-256 hash. A leaked database dump
then yields no usable credentials. SHA-256 rather than BCrypt because the token is a
high-entropy random value, not a low-entropy password — it is not brute-forceable, so a slow
KDF buys nothing but latency on every refresh.

*Alternative considered:* storing the token verbatim, for simpler debugging. Rejected: it
turns read access to the database into session takeover for every live session.

### Reuse detection revokes the family, accepting that the legitimate client is logged out

When a consumed token is presented, one of two parties holds the newest credential and there
is no way to tell which. Revoking the family fails safe: the attacker's session ends, and so
does the victim's, forcing a re-authentication the attacker cannot complete. Being signed out
unexpectedly is a far better outcome than an undetected hijack, and the spec states this
consequence explicitly rather than hiding it.

### `ITokenGenerator` grows a separate refresh-token method

`Generate(AppUser)` stays synchronous and JWT-only. A new
`GenerateRefreshToken()` returns the raw token and its hash as a pair; persistence stays in
`IIdentityService`, which already coordinates stores. This keeps the generator a pure
function of randomness and the service the only thing that writes.

*Alternative considered:* making `ITokenGenerator` async and having it persist. Rejected —
it would give a token generator a database dependency and blur the Application layer's
orchestration role.

### `TokenDto` carries the refresh token; the controller decides it becomes a cookie

`IIdentityService` returns the refresh token in `TokenDto`, and `AuthController` moves it
into a `Set-Cookie` header instead of serialising it. Cookie semantics are an HTTP transport
concern, and Application must not know about `HttpContext`. The API layer therefore maps to a
response DTO without the refresh token, which is what keeps the requirement "never appears in
a response body" enforceable in one place.

### Cookie attributes differ by environment

`HttpOnly` and `SameSite=Lax` always; `Secure` follows the environment, since the development
API is plain HTTP on localhost and a `Secure` cookie would simply never be sent. `Lax` rather
than `Strict` so that the Google OAuth redirect back into the app still carries the cookie.
`Path=/auth` limits the cookie to the endpoints that need it.

### Silent renewal is single-flight on the client

`authService` holds one in-flight refresh promise; concurrent callers await it rather than
starting their own. This is the same pattern — and the same failure mode — as the connect
race fixed in `signalRService`: the shared promise must be cleared inside the attempt so a
later caller after a failure gets a fresh try rather than an abandoned rejection.
`accessTokenFactory` becomes async and awaits that promise instead of throwing immediately.

*Alternative considered:* a background timer refreshing on a schedule. Rejected — it keeps
firing in dead tabs and still needs the on-demand path for a token that lapsed while the
machine was asleep.

## Risks / Trade-offs

- **A legitimate user is signed out when a replay is detected** → Accepted deliberately; see
  the decision above. The alternative is an undetectable hijack.
- **A network failure mid-rotation can consume a token whose successor never reaches the
  client** → The user re-authenticates. Mitigating this properly requires a grace window
  where the previous token stays briefly valid, which reopens the replay hole; not worth it
  at this scale.
- **`SameSite=Lax` is weaker than `Strict`** → Required for the OAuth redirect. The refresh
  endpoint is a `POST`, which `Lax` does not send cross-site, so the exposure is limited.
- **Refresh tokens accumulate in MongoDB** → A TTL index removes them after expiry plus a
  retention margin.
- **`accessTokenFactory` becoming async changes SignalR reconnect timing** → Covered by the
  existing connect-lifecycle tests, which already exercise a connect that resolves late.
- **Google OAuth callback** → This was expected to be a redirect needing the cookie set on
  the redirect response. It is not: the callback returns the token as JSON through the same
  code path as login, so it needed no special handling. Corrected after implementation.

## Migration Plan

No data migration: the `refreshTokens` collection starts empty and is created on first write.

Existing sessions hold an access token with no refresh cookie. Those users keep working until
their token lapses, then sign in once and receive a refresh cookie. Nothing needs to be
invalidated at deploy time.

Rollback is to redeploy the previous build. The orphaned collection is inert, and clients
fall back to the current behaviour of ending the session at expiry.

## Open Questions

- Refresh-token lifetime and the staleness margin that triggers early renewal are settings,
  not structure. Sensible starting values (14 days; renew with under 5 minutes remaining) go
  into configuration and can be tuned without touching the specs or the task breakdown.

**Resolved after implementation.** The shipped values are a one-day refresh lifetime and a
one-minute renewal margin, against seven-minute access tokens. The margin never became a
server setting: the client owns it, because it is the client that decides when to renew, and
a configured value that nothing read was worse than no setting at all.

A day rather than a fortnight because the refresh token is the only long-lived credential
here and revocation depends on someone noticing a theft. The prediction that these are
settings rather than structure held: changing them touched configuration and one default, and
no requirement in the spec, which states retention and expiry without naming a duration.
