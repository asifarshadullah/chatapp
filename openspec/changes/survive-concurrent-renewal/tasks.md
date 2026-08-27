Every task starts with a failing test. Where a task fixes a defect, the test must be *watched
failing* against the current code before the implementation is written — the second-tab
regression in the previous change shipped past a fully green suite, and only a test seen
failing proves it would have caught it.

Group 3 is the regression proof. The browser test in group 6 is a smoke test: it cannot
reliably arrange millisecond overlap, so it must not be relied on to catch this defect.

## 1. Preserving the pre-consumption expiry

- [x] 1.1 Write a failing test in `RefreshTokenTests` that consuming a credential preserves
      the expiry it had beforehand, while `ExpiresAt` is still pulled in to now. Verify: it
      fails to compile against today's entity, then passes.
- [x] 1.2 Add `PreConsumptionExpiresAt` and set it in `Consume`. Verify: 1.1 passes and every
      existing `RefreshTokenTests` case still passes.
- [x] 1.3 Write a failing test that a credential consumed after it had already lapsed
      preserves the earlier expiry, not now. Verify: it passes, matching the existing rule
      that `Consume` never pushes expiry outwards.
- [x] 1.4 Add the field to `RefreshTokenDocument` and its mapping, with a failing store test
      that it round-trips and that a document written without it loads as the default.
      Verify: both pass — this is what makes the deploy migration-free.

## 2. The grace predicate

- [x] 2.1 Write a failing test that a credential consumed one second ago is within a
      two-second grace window and one consumed a minute ago is not. Verify: fails to compile,
      then passes.
- [x] 2.2 Add `IsWithinGrace(DateTime now, TimeSpan grace)` to `RefreshToken`. Verify: 2.1
      passes.
- [x] 2.3 Write failing tests that an unconsumed credential is never within grace, and that a
      revoked one is not either however recently it was consumed. Verify: both pass, or the
      predicate is corrected until they do.
- [x] 2.4 Write a failing test that the window is anchored to `ConsumedAt`: a credential
      consumed at T is outside grace at T+60 however many times the predicate was asked in
      between. Verify: it passes, confirming the window cannot be walked forward.

## 3. Conditional consume in the store

- [x] 3.1 Write a failing test in `MongoRefreshTokenStoreTests` that `TryConsumeAsync` returns
      true for an unconsumed credential and persists the consumption. Verify: fails to
      compile, then passes.
- [x] 3.2 Add `TryConsumeAsync` to `IRefreshTokenStore` and implement it in
      `MongoRefreshTokenStore` as an update filtered on id and `ConsumedAt == null`. Verify:
      3.1 passes; `UpdateAsync` stays on the interface for test arrangement.
- [x] 3.3 Write a failing test that a second `TryConsumeAsync` against the same credential
      returns false and leaves the first consumption's timestamp untouched. Verify: it fails
      against an unguarded replace — this is the lost update, and it must be watched failing.
- [x] 3.4 Write a failing test in `IdentityServiceRefreshTests`'s fake store mirroring the
      same contract, so the service tests exercise the real semantics. Verify: the fake
      rejects a second consume exactly as Mongo does.

## 4. The grace exchange in the identity service

- [x] 4.1 Write a failing test that presenting a credential consumed within the grace window
      succeeds, returns an access token, and leaves the family unrevoked. Verify: it fails
      against today's code with the family revoked — the regression proof, watched failing.
- [x] 4.2 Move the user lookup above the consumed-credential branch, with no behaviour change.
      Verify: the whole existing suite still passes and 4.1 still fails.
- [x] 4.3 Route the refresh path through `TryConsumeAsync`, taking the grace check on false.
      Verify: 4.1 passes and every existing replay and rotation test passes untouched.
- [x] 4.4 Write failing tests that the grace-issued credential is in the same family, is
      exchangeable, and carries the session's chosen length. Verify: all pass.
- [x] 4.5 Write a failing test that losing the race outside the grace window revokes the
      family, and one that a credential whose family is already revoked is refused even
      within the window. Verify: both pass — grace narrowed the replay rule, not removed it.
- [x] 4.6 Write a failing test that the presented credential's `ConsumedAt` is unchanged after
      a grace exchange. Verify: it passes.

## 5. Bounding the grace-issued credential

Task 5.4 found that the bound survived only one exchange — a grace-issued credential is
otherwise ordinary, so the next renewal slid the session back to full length. The design was
corrected mid-implementation to inherit the ceiling through every successor; see design.md.

- [x] 5.1 Write a failing test that a grace-issued credential expires no later than its
      predecessor's `PreConsumptionExpiresAt`, using a remembered session so the uncapped
      value would be visibly far out. Verify: it fails with a full thirty-day expiry, then
      passes.
- [x] 5.2 Apply the cap in the grace branch. Verify: 5.1 passes.
- [x] 5.3 Write a failing test that an *ordinary* rotation is not capped and still slides its
      full lifetime from now. Verify: it passes — this guards the requirement that continued
      use keeps a session alive indefinitely.
- [x] 5.4 Write a failing test that renewing repeatedly under grace cannot extend the session
      past the original bound. Verify: it passes.

## 6. Settings, logging, and the browser

- [x] 6.1 Write a failing test that `IRefreshTokenSettings` exposes a grace window defaulting
      to two seconds, then add `GraceWindow` and `GraceWindowSeconds`. Verify: it passes;
      update `FakeRefreshTokenSettings` with a value distinct from the default so tests can
      tell which was used.
- [x] 6.2 Write failing cases in `RefreshTokenSettingsStartupTests` that startup is refused
      for a non-positive grace window and for one not shorter than the ordinary lifetime,
      each naming the setting, then add the `.Validate(...)` calls. Verify: they pass and the
      existing startup cases are unaffected.
- [x] 6.3 Write a failing test that a grace exchange emits a warning naming the family id,
      using a capturing `ILogger<IdentityService>` fake, then inject the logger and log in
      the grace branch. Verify: it passes; update every construction of `IdentityService`
      across the test projects.
- [x] 6.4 Add a Playwright test in `session.spec.ts` that opens a second tab, renews in both,
      and asserts both stay signed in. Verify: it passes — and comment it as a smoke test
      that cannot arrange the overlap, so nobody later mistakes it for the regression proof.

## 7. Verify and close out

- [x] 7.1 Run the suites CI runs — `npm run typecheck`, `npm run lint`, `npm run test:run` in
      `frontend/chat-ui`, and `dotnet test backend/ChatApp.sln` — plus `npx playwright test`
      locally, since this touches the session path and e2e is not in CI. Verify: all green.
- [ ] 7.2 Confirm the shipped behaviour matches the delta spec, correcting the spec if the
      implementation taught otherwise, then archive. Verify: `openspec validate` passes and
      `openspec/specs/identity/token-refresh/spec.md` describes what the code does.
- [ ] 7.3 Close issue #1 referencing the commit; note on issue #3 that a family may now hold
      more than one active chain. Verify: both read correctly on GitHub.
