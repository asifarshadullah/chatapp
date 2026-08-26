Each task begins by writing a test that fails for the stated reason, then makes it pass. A
test that passes before the implementation is written proves nothing and must be corrected
before continuing.

## 1. Domain — the RefreshToken entity

- [x] 1.1 Write failing tests for `RefreshToken` covering creation, `IsUsable(now)` false once
      expired, false once consumed, and false once revoked; implement the entity in
      `Chat.Identity.Domain` with the two-constructor pattern until they pass
- [x] 1.2 Write a failing test that `Consume()` on an already-consumed token is rejected, and
      that `Revoke()` is idempotent; implement until it passes
- [x] 1.3 Write a failing test that a reconstructed consumed-and-expired token round-trips
      through the reconstruction constructor without tripping its own guards — the trap
      recorded in `docs/architecture/scenarios/01-archive-conversation/retrospective.md`

## 2. Application — contracts

- [x] 2.1 Add `IRefreshTokenStore` to `Chat.Identity.Application` (find by hash, add, update,
      revoke family) and verify the solution builds with no Infrastructure reference added to
      Application: `dotnet build backend/ChatApp.sln`
- [x] 2.2 Extend `ITokenGenerator` with refresh-token generation returning the raw token and
      its hash; write a failing test that two calls produce different, high-entropy values and
      that the hash is not the raw token; implement until it passes
- [x] 2.3 Add `RefreshAsync` and `LogoutAsync` to `IIdentityService`, and carry the refresh
      token on `TokenDto`; verify the solution builds

## 3. Application — exchange and rotation behaviour

- [x] 3.1 Write failing tests, against a fake `IRefreshTokenStore`, that `RefreshAsync`
      returns a new access token for a valid credential and rejects expired, unknown, and
      missing ones without disclosing which; implement until they pass
- [x] 3.2 Write a failing test that a successful exchange consumes the presented token and
      issues a distinct successor sharing its family; implement rotation until it passes
- [x] 3.3 Write a failing test that presenting a consumed token revokes every token in that
      family, including the newest, and that a second family for the same user is untouched;
      implement reuse detection until it passes
- [x] 3.4 Write a failing test that `LogoutAsync` revokes the family and that logging out with
      no credential succeeds without error; implement until it passes
- [x] 3.5 Write failing tests that register, login, and external-callback each issue a refresh
      token alongside the access token; implement until they pass

## 4. Infrastructure — persistence

- [x] 4.1 Write a failing integration test, against the Docker Compose MongoDB used by the
      existing store tests, that `MongoRefreshTokenStore` round-trips a token through
      `FromDomain`/`ToDomain` with consumption and revocation state intact; implement until it
      passes
- [x] 4.2 Write a failing test that revoking a family updates every member in one operation;
      implement until it passes
- [x] 4.3 Create the index on the token hash and the TTL index that reaps expired tokens after
      the retention margin; verify by inspecting `getIndexes()` on the `refreshTokens`
      collection
- [x] 4.4 Add refresh lifetime and renewal-margin settings to configuration alongside
      `JwtSettings`, and verify they bind by asserting the configured values in a test

## 5. API — endpoints and cookie handling

- [x] 5.1 Write a failing API test that `/auth/register` and `/auth/login` set a refresh cookie
      marked `HttpOnly`, and that the refresh token does not appear in the response body;
      implement the cookie mapping in `AuthController` until it passes
- [x] 5.2 Write a failing API test that `POST /auth/refresh` exchanges the cookie for a new
      access token and returns a rotated cookie; implement until it passes
- [x] 5.3 Write a failing API test that `/auth/refresh` returns 401 for missing, expired, and
      replayed credentials alike; implement until it passes
- [x] 5.4 Write a failing API test that `POST /auth/logout` clears the cookie and that a later
      exchange with that credential is refused; implement until it passes
- [x] 5.5 Write a failing test that the Google OAuth callback sets the cookie on its redirect
      response — the path design.md flags as easy to miss; implement until it passes
- [x] 5.6 Verify `Secure` is set outside development and omitted in development, by asserting
      the cookie options under both environments

## 6. Frontend — silent renewal

- [x] 6.1 Write a failing test that `authService.refresh()` posts to `/auth/refresh` with
      credentials included and stores the returned access token; implement until it passes
- [x] 6.2 Write a failing test that concurrent callers share a single in-flight refresh, and
      that a caller arriving after a failed refresh starts a fresh attempt rather than
      receiving the abandoned rejection — the same failure mode fixed in `signalRService`;
      implement the single-flight guard until it passes
- [x] 6.3 Write a failing test that `getToken()` renews when the token is within the staleness
      margin and returns the renewed token; implement until it passes
- [x] 6.4 Write a failing test that a refused renewal reports an ended session and returns the
      user to sign in, distinct from an unreachable server; implement until it passes
- [x] 6.5 Write a failing test that `accessTokenFactory` awaits an in-flight renewal instead of
      throwing `SessionExpiredError`, and that the hub connects afterwards; make it async until
      it passes
- [x] 6.6 Write a failing test that no renewal is attempted when no session exists; implement
      until it passes

## 7. Verification

- [x] 7.1 Run the checks CI runs and confirm all pass: `npm run typecheck`, `npm run lint`,
      `npm run test:run` in `frontend/chat-ui`, and `dotnet test backend/ChatApp.sln`
- [x] 7.2 Add a Playwright test that a session survives access-token expiry mid-conversation —
      shorten the configured lifetime for the run rather than waiting an hour — and confirm
      the user is neither signed out nor loses the conversation
- [x] 7.3 Run `npx playwright test` in `e2e/playwright` and confirm the existing suite still
      passes, since it is not covered by CI
- [x] 7.4 Verify each new test fails against the pre-change code, per CLAUDE.md; correct or
      drop any that passes without the implementation
