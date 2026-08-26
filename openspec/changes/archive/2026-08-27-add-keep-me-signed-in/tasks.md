Each task starts as a failing test (see the tdd skill); verify the test fails
against the current code before making it pass.

## 1. Configuration

- [x] 1.1 Add `PersistentLifetimeDays` (default 30) to `IRefreshTokenSettings` and
  `RefreshTokenSettings`, verified by a `Chat.Identity.Tests` test asserting the
  default and that a bound configuration value overrides it
- [x] 1.2 Fail startup when the persistent lifetime is not longer than the ordinary
  one, verified by a `Chat.Api.Tests` test that the host refuses to start on that
  configuration and starts on a valid one
- [x] 1.3 Add the setting to `appsettings.json` and `appsettings.Development.json`
  and verify `dotnet build` plus the existing suites still pass

## 2. Domain

- [x] 2.1 Add `Persistent` to `RefreshToken` (both constructors), verified by a
  domain test that a token issued persistent reports it and one issued without it
  does not
- [x] 2.2 Round-trip `Persistent` through `RefreshTokenDocument` and
  `MongoRefreshTokenStore`, verified by a store test that a stored persistent token
  reads back persistent
- [x] 2.3 Read a stored document with no `Persistent` field as not persistent,
  verified by a store test that deserializes a legacy document shape

## 3. Session issuance

- [x] 3.1 Thread the choice through `IIdentityService.LoginAsync`,
  `RegisterAsync` and `HandleExternalCallbackAsync` into `IssueSessionAsync`, and
  select the lifetime from it — verified by service tests asserting the stored
  `ExpiresAt` uses the ordinary lifetime when not chosen and the extended one when
  chosen, for all three entry points
- [x] 3.2 Carry the refresh credential's absolute expiry on `TokenDto` alongside
  the raw value, verified by a service test that the returned expiry matches the
  stored token's `ExpiresAt`
- [x] 3.3 Make a successor inherit `Persistent` and recompute its expiry from the
  time of exchange, verified by a `RefreshAsync` test that an extended session's
  successor is extended and expires later than its predecessor
- [x] 3.4 Verify an ordinary session's successor stays ordinary, and that a legacy
  token with no `Persistent` value rotates into an ordinary successor

## 4. HTTP surface

- [x] 4.1 Add the flag to `LoginDto` and `RegisterDto` and pass it to the service,
  verified by `Chat.Api.Tests` integration tests for `/auth/login` and
  `/auth/register`; a request body omitting the flag must behave as ordinary
- [x] 4.2 Set the refresh cookie's `Expires` from the expiry on `TokenDto`, and omit
  `Expires` for an ordinary session, verified by integration tests inspecting the
  `Set-Cookie` header on both paths and on `/auth/refresh`
- [x] 4.3 Carry the choice through `AuthenticationProperties.Items` on the Google
  challenge and read it back in `GoogleCallback`, verified by tests that the
  challenge carries the item and that a callback with no readable choice issues an
  ordinary session

## 5. Frontend

- [x] 5.1 Send the flag from `authService.login` and `authService.register`,
  verified by `authService.test.ts` asserting the request body for both states
- [x] 5.2 Store the session marker in `sessionStorage` for an ordinary session and
  `localStorage` for a remembered one, and read either when deciding whether to
  restore — verified by `authServiceRefresh.test.ts` covering restore-after-reload
  for both and no restore attempt once the ordinary marker is gone
- [x] 5.3 Clear both markers on `logout` and on a refused refresh, verified by an
  existing-behaviour test extended to both storages
- [x] 5.4 Add the "Keep me signed in" control to the sign-in and registration forms,
  unchecked by default, verified by a React Testing Library test that it renders
  unchecked and that its state reaches `authService`
- [ ] 5.5 Pass the current choice to the Google sign-in link, verified by a test
  asserting the target the button navigates to reflects the checkbox — NOT DONE.
  The frontend has no Google sign-in UI at all: no button, no link to
  `/auth/google`, and no handling of the callback landing back in the app.
  Building one is a separate feature, so it was left out by decision rather
  than overlooked. The backend half is complete and tested: `/auth/google`
  accepts `?staySignedIn=`, carries it through the provider round trip in the
  authentication properties, and the callback honours it — a future Google
  button only has to put the checkbox's value in that query string.

## 6. Verification

- [x] 6.1 Add a Playwright test in `e2e/playwright/tests/session.spec.ts` covering a
  remembered session surviving a browser-context restart and an ordinary one not
- [x] 6.2 Run `npm run typecheck`, `npm run lint`, `npm run test:run` in
  `frontend/chat-ui`, `dotnet test backend/ChatApp.sln`, and
  `npx playwright test` in `e2e/playwright`, and confirm all pass

## 7. Corrections from the design review

- [x] 7.1 Record the session kind alongside the marker in `localStorage` instead of
  splitting across `sessionStorage`, verified by a test that a second tab of an
  ordinary session is signed in rather than shown the sign-in form
- [x] 7.2 On returning after a browser restart, treat an ordinary session's marker
  as spent without attempting a renewal, verified by tests that a remembered
  session renews and an ordinary one goes straight to sign-in with no request
- [x] 7.3 Rewrite a credential's `ExpiresAt` to the replay-detection window when it
  is consumed, verified by a service test that a consumed credential's stored
  expiry is pulled in, and a store test that a replay within the window is still
  found and revokes the family
- [x] 7.4 Update the e2e restart tests for the marker change and re-run
  `npx playwright test`, plus the full CI set, and confirm all pass

Deferred deliberately, not overlooked: surviving concurrent renewal across tabs.
It is described in proposal.md under Non-goals and in design.md under Risks, but
deliberately NOT written as a requirement in this change's delta — the code does
not meet it, and archiving it would leave the main spec asserting behaviour the
system lacks. It needs its own change: a cross-tab lock, or a server-side grace
window that returns the successor to a just-consumed credential.

