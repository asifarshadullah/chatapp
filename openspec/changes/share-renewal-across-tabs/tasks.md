Every task starts with a failing test, and a test that guards a defect must be *watched
failing* against the current code before the implementation is written. That matters
particularly here: all of this behaviour is dormant with one tab open, so a test that passes
without staging a sibling's write proves nothing.

"A sibling tab renewed" is staged as a `localStorage` write from inside the mocked `fetch` —
between the exchange being issued and its outcome arriving. No second browser context is
needed, and none should be introduced.

Group 3 is the one that would have caught issue #5. Group 1 must land first: without the
token-identity comparison, the reuse check breaks `authorizedFetch`'s 401 retry. Group 2 must
land before group 3 uses it, because raising the wrong error on the refusal path revokes the
credential family server-side and would be worse than the bug.

## 1. Telling a superseded token from a repudiated one

- [x] 1.1 Write a failing test in `authServiceRefresh.test.ts` that `refresh(staleToken)`
      returns a usable token a sibling stored, without calling `fetch`. Verify: watch it fail
      against today's code, which always exchanges.
- [x] 1.2 Give `refresh` an optional superseded-token parameter defaulting to the value in
      storage at entry, and return a stored token that is present, differs from it, and is not
      stale. Verify: 1.1 passes and every existing `authServiceRefresh` case still passes
      unchanged — the no-argument callers are the compatibility guard.
- [x] 1.3 Write a failing test that a stored token which is itself stale is not adopted, and
      the exchange proceeds. Verify: passes, and `fetch` is called exactly once.
- [x] 1.4 Write a failing test in `authorizedFetch.test.ts` that a 401 retry does not re-send
      the token the server just rejected, even though that token is stored and unexpired.
      Verify: watch it fail against a naive "reuse whatever is stored" implementation — if it
      passes before 1.2, the reuse check is not token-identity based and 1.2 is wrong.

`getValidToken` also passes the token it read, but no task covers it: that path reads storage
synchronously and returns a usable sibling token by its own early exit, so the reuse check
cannot fire there and any test of it would pass by construction.
- [x] 1.5 Pass the rejected token to `refresh` from `authorizedFetch`. Verify: 1.5 passes and
      the existing single-retry-never-a-loop tests still pass.

## 2. A failure that does not end the session

- [x] 2.1 Write a failing test that `RenewalFailedError` is distinct from `SessionExpiredError`
      and is not an instance of it. Verify: fails to compile, then passes.
- [x] 2.2 Add `RenewalFailedError` to `sessionErrors.ts`. Verify: 2.1 passes.
- [x] 2.3 Write a failing test that `ChatWindow` shown a `RenewalFailedError` from a failed
      connect reports the transient message and does NOT call `onSessionExpired`. Verify:
      passes without touching `ChatWindow` — this asserts the property the design relies on,
      so that a future consumer cannot quietly make it fatal.
- [x] 2.4 Write the same failing test for `signalRService`'s error mapping. Verify: passes
      unchanged, for the same reason.

## 3. Surviving a superseded refusal

- [x] 3.1 Write a failing test that a refused exchange does NOT clear `auth_token`,
      `auth_token_expiry` or `auth_session` when a sibling stored a different usable token
      while the exchange was in flight, and that `refresh` resolves with that token. Verify:
      watch it fail — today's `clearLocal()` on any non-OK response is exactly what it catches.
- [x] 3.2 Capture the token at entry, and on a non-OK response re-read storage and return a
      differing usable token instead of clearing. Verify: 3.1 passes.
- [x] 3.3 Write a failing test that a refusal whose evidence is a *stale* differing token
      leaves storage intact and raises `RenewalFailedError`, not `SessionExpiredError`.
      Verify: watch it fail — the single-predicate implementation clears here, which is the
      sign-out this change exists to prevent.
- [x] 3.4 Split the predicate: evidence is present-and-different, usable is evidence-and-not-
      stale. Verify: 3.3 passes and 3.1 still passes.
- [x] 3.5 Write a failing test that a refusal with nothing different stored still clears the
      session and raises `SessionExpiredError`. Verify: passes — the guard that keeps a
      revoked session ending.
- [x] 3.6 Write a failing test that a refusal after another tab signed out — the stored token
      removed, not replaced — clears the session and raises `SessionExpiredError`. Verify:
      watch it fail if absence is treated as evidence; this is the null guard.
- [x] 3.7 Write a failing test that a client continuing on a sibling's token after a revocation
      is signed out once that token lapses and its own renewal is refused with nothing behind
      it. Verify: passes, bounding the exposure named in design.md.

## 4. Verification

- [x] 4.1 Run `npm run typecheck`, `npm run lint` and `npm run test:run` in
      `frontend/chat-ui`. Verify: all green.
- [x] 4.2 Run the Playwright suite locally against a running stack. Verify: the chat path is
      unbroken. Smoke test only — it cannot stage a refusal more than two seconds late, and
      must not be relied on to catch this defect.
- [x] 4.3 Confirm in two real browser tabs that a session survives what previously ended it.
      Verify: done with a throwaway Playwright spec that staged the interleaving — tab A's
      renewal landing while tab B's exchange was in flight, B's refusal forced by a route
      interception. Both tabs stayed signed in; the same spec run against the pre-fix
      `authService` left tab B on the sign-in form, which is the regression proof. The spec
      was deleted rather than kept: it depends on a forced 401 and on interleaving that only
      holds by construction, so it would guard the arrangement rather than the behaviour.
