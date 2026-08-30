Every task starts with a failing test, and a test that guards a defect must be *watched
failing* against the current code before the implementation is written.

The central assertion here is a request that must **not** happen, which is weak on its own — it
would pass against code that never renews at all. Group 3 is what actually demonstrates the
harm is gone: against a real backend, the refresh credential still exchanges after a client has
abandoned its session. That test is keepable, unlike the disposable reproduction the previous
change needed, because it stages nothing by construction.

## 1. Abandoning a session without revoking it

- [x] 1.1 Write a failing test that a session ended by itself makes no request to
      `/auth/logout`, while still clearing `auth_token`, `auth_token_expiry` and `auth_session`.
      Verify: watch it fail — today's `endSession` POSTs on both paths, so the assertion on the
      absent request is what fails, not the clearing.
- [x] 1.2 Add `abandonSession` to `App`, stopping the hub and calling `authService.clearLocal`,
      and wire `onSessionExpired` to it. Verify: 1.1 passes.
- [x] 1.3 Write a failing test that the Sign out button still POSTs `/auth/logout`. Verify:
      passes — this is the guard that 1.2 narrowed the right path, and it should already pass.
- [x] 1.4 Write a failing test that an abandoned session still shows the sign-in form and
      leaves no access token behind. Verify: passes — the user-visible outcome is unchanged,
      which is what makes this safe to ship.

## 2. The hub is stopped either way

- [x] 2.1 Write a failing test that abandoning a session stops the hub connection. Verify:
      watch it fail against an implementation that only clears local state — a live hub against
      a dead session reconnects and re-raises the same error.

## 3. Proving the credential survives

- [x] 3.1 Write a Playwright test that signs in, drives the client to abandon its session, and
      then shows the refresh credential still exchanges — a direct `POST /auth/refresh` from
      the page returns a new token. Verify: watch it fail against today's code, where the
      logout POST has already revoked the family. This is the regression proof; the unit tests
      only assert a request is absent.

## 4. Verification

- [x] 4.1 Run `npm run typecheck`, `npm run lint` and `npm run test:run` in
      `frontend/chat-ui`. Verify: all green.
- [x] 4.2 Run `dotnet test backend/ChatApp.sln`. Verify: green — the backend is untouched, so
      this is a guard against having changed it by accident.
- [x] 4.3 Run the full Playwright suite locally against a running stack. Verify: the chat and
      session paths are unbroken, including the new test from 3.1.
