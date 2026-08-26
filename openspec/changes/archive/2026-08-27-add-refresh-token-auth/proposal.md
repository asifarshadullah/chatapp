## Why

Access tokens expire after 60 minutes (`Jwt.ExpiryMinutes`) and nothing renews them, so a
session simply ends mid-use. The user is returned to the sign-in form while a conversation is
open, and any assistant reply still streaming is lost. Today's behaviour is only a graceful
failure — `SessionExpiredError` reports the expiry honestly instead of claiming the server is
unreachable — but the session still dies.

The obvious fix, a longer access-token lifetime, trades one problem for a worse one: a leaked
token stays valid for as long as it lives, and there is no way to revoke it. A refresh token
keeps access tokens short-lived while letting the session continue.

This change belongs to the **Identity** context. It concerns who the user is and how long
that claim stays valid, which is Identity's responsibility; Chat and Billing consume the
resulting identity and are unaffected.

## What Changes

- Issue a refresh token alongside the access token on registration, password login, and
  Google OAuth callback.
- Deliver the refresh token as a `Secure`, `httpOnly`, `SameSite` cookie. It is never
  readable by JavaScript, so an XSS bug cannot exfiltrate a long-lived credential. The access
  token continues to live in `localStorage` and stays short-lived.
- Add `POST /auth/refresh`, which reads the cookie, validates the token, and returns a new
  access token plus a rotated refresh cookie.
- Rotate on every use: refreshing invalidates the presented token and issues a successor.
- Detect reuse: presenting an already-consumed token means it was captured and replayed, so
  the entire token family is revoked and the user must sign in again.
- Add `POST /auth/logout`, which revokes the current token family and clears the cookie.
  Logout is currently client-only, which leaves the refresh token live after signing out.
- Persist refresh tokens in MongoDB with their family, consumption state, and expiry.
- Renew silently on the frontend: `authService` refreshes before the access token lapses and
  once on a 401, and `signalRService`'s `accessTokenFactory` awaits an in-flight refresh
  rather than failing the hub connection.
- **BREAKING** for API clients: `/auth/register`, `/auth/login`, and the Google callback now
  set a cookie that clients must return on `/auth/refresh`. The `TokenDto` response body is
  unchanged, so the existing frontend contract still holds.

## Capabilities

### New Capabilities

- `identity/token-refresh`: issuing, rotating, revoking and validating refresh tokens, and
  the silent renewal of access tokens that depends on them.

### Modified Capabilities

None. No spec exists under `openspec/specs/` yet; this is the first capability documented,
and it does not change the requirements of any other.

## Non-goals

- **No sliding or configurable session policy.** Refresh-token lifetime is a fixed setting,
  not a per-user or per-plan policy. Billing stays uninvolved.
- **No "remember me" or device management.** No UI for listing or revoking sessions on other
  devices, though the token-family model is what would later make that possible.
- **No move of the access token out of `localStorage`.** Keeping it there is a deliberate
  trade-off: it is short-lived, and moving it to a cookie would require solving CSRF for
  every authenticated request rather than only the refresh endpoint.
- **No change to `Jwt.ExpiryMinutes`.** Tuning the access-token lifetime is a separate
  decision from making renewal possible.
- **No revocation of access tokens.** A JWT stays valid until it expires; only refresh tokens
  are revocable. Shortening the access-token lifetime is the lever for that, not this change.

## Impact

**Identity domain** — a new `RefreshToken` entity with its family identifier, consumption
state, and expiry; revocation is a domain operation.

**Identity application** — `ITokenGenerator` gains refresh-token generation; a new
`IRefreshTokenStore` for persistence; `IIdentityService` gains `RefreshAsync` and
`LogoutAsync`. `TokenDto` gains the refresh token so the API layer can set the cookie.

**Identity infrastructure** — `JwtTokenGenerator` produces refresh tokens;
a Mongo-backed `IRefreshTokenStore` with an index on the token and its family.

**API** — `AuthController` sets and clears the cookie and gains `/auth/refresh` and
`/auth/logout`. Cookie options differ between development (HTTP on localhost) and
production.

**Frontend** — `authService` gains silent refresh and a single-flight guard so concurrent
callers share one renewal; `signalRService.accessTokenFactory` awaits it; `App` no longer
treats every expiry as the end of a session.

**Persistence** — a new `refreshTokens` collection, plus a TTL index so expired tokens are
reaped rather than accumulating.

**Security** — reuse detection is the central control. It is what turns a stolen refresh
token from a silent, indefinite session hijack into a detectable event that ends the family.
