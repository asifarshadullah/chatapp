## Why

A refresh credential is consumed on every exchange, and re-presenting a consumed credential
is read as a replay: the family is revoked and the user is signed out everywhere. Two clients
of one session can present the same credential without anyone having stolen anything, and
today that ends the session.

The credential lives in one cookie jar shared by every tab, so tabs cannot hold stale
credentials — each request carries whatever the jar holds at that moment. The collision is
narrower than it first appears, and entirely in-flight: one client sends the credential, a
second client's exchange consumes it and updates the jar, and the first request arrives at a
credential that is already spent. Resuming a machine with several tabs open is the case that
produces it, because they all renew at once.

Underneath that sits a defect the grace window alone would not fix. Consumption is an
unconditional overwrite of the stored record, so two exchanges that overlap closely enough
both read the credential as unconsumed and both succeed — a lost update. That accident
currently hides the collision in the simultaneous ordering rather than resolving it, and it
means the system does not reliably detect concurrent use at all. Anything layered on top of
replay detection inherits that.

## What Changes

- **Consumption becomes conditional.** A credential is consumed by an update guarded on its
  being unconsumed. Exactly one of two overlapping exchanges wins; the loser is told it lost
  rather than silently overwriting the winner.
- **The loser is judged by a grace window.** A credential consumed within the last couple of
  seconds is the legitimate holder renewing concurrently, and the exchange succeeds with a
  fresh credential in the same family. Outside that window the family is revoked, exactly as
  a replay is today.
- **A credential issued on the grace path cannot outlive the session it belongs to.** It is
  bounded by the expiry its predecessor had before consumption, and that bound is inherited by
  every credential the session issues afterwards — otherwise one ordinary renewal would restore
  a full lifetime and a replayed credential would escape it for the price of one more request.
- The grace window is configuration, validated at startup alongside the existing lifetimes.
- Each grace exchange is logged with its family, so a genuine replay attack — repeated grace
  hits on one family — stays visible rather than being absorbed.

**Not breaking.** The only specified behaviour that narrows is replay detection, which now
begins after the grace window instead of immediately. Two fields are added to the stored
record; absent values deserialize to null, which the code reads as "no ceiling known". No API contract
changes and no frontend change.

### The grace exchange issues a sibling, not the successor

The issue that prompted this change proposed returning *the successor* to the losing client.
That is not possible here. Only a hash of each credential is stored — the raw value goes to
the client and is never kept — so by the time the loser is judged, the successor's usable form
exists only at the winner. Returning it would mean storing raw credentials, defeating the
property that a leaked database yields nothing usable.

The grace exchange therefore issues a **new credential in the same family**. This works
because the credential lives in a shared cookie rather than per-client storage: both responses
write the same jar, the last one wins, and the browser converges on a single credential. The
other sibling is never presented and lapses on its own.

## Capabilities

### New Capabilities

None. This corrects behaviour inside an existing capability.

### Modified Capabilities

- `identity/token-refresh`: replay detection gains a grace window, and consumption becomes
  well-defined under concurrency. Two existing requirements change — "Rotation on every
  exchange", whose flat claim that a consumed credential can never be exchanged again is now
  qualified, and "Replay of a consumed credential revokes the session family". Two are added:
  one for surviving concurrent renewal, one for what a grace-issued credential may not do.

## Bounded context

Identity. The defect is in how `Chat.Identity` consumes credentials and decides that one was
replayed; no other context sees refresh credentials. The entity, the store contract, the
settings contract and the identity service all live there, and `Chat.Api` changes only in
binding and validating one more configuration value.

## Non-goals

- **Fixing the client's contribution to the same symptom.** A refused renewal in one tab
  clears `localStorage`, which is shared, so it signs out every open tab; and a tab never
  re-reads a token another tab has just fetched. That is a real second cause of the same
  user-visible failure, and it survives this change. It is deliberately separate: mixing
  client state management into a change about replay semantics makes both harder to review.
  Tracked as issue #5.
- **A client-side cross-tab lock.** BroadcastChannel or a storage mutex would keep replay
  detection strict, but protects only browsers that cooperate. The fix belongs on the server,
  where it holds for every client.
- **An absolute session cap.** A session acquires a ceiling only by renewing under the grace
  window. One that never does is never bounded, and ordinary rotation keeps sliding, as decided
  in the previous change.
- **Capping how many times one credential may be grace-exchanged.** Logging makes an abusive
  pattern visible; acting on it automatically is more state for a threat the window already
  bounds to seconds.
- **Revoking the orphaned sibling.** The server cannot tell which of two live siblings the
  browser kept.
- **The other gaps recorded alongside this one** — session revocation for a lost device,
  password reset revoking sessions, and the missing Google sign-in affordance.

## Impact

- `RefreshToken` — remembers the expiry it had before consumption, carries an inherited session
  ceiling, and answers whether it is within a grace window.
- `IRefreshTokenStore` / `MongoRefreshTokenStore` — a conditional consume operation.
- `IRefreshTokenSettings` / `RefreshTokenSettings` — one new value, with startup validation.
- `IdentityService.RefreshAsync` — consumes conditionally and handles losing; gains a logger,
  which the codebase does not currently have anywhere.
- `Program.cs` — binds and validates the new setting.
- No frontend change. No migration: the new field defaults on records written before it.
- Tests: the entity's unit tests, the Mongo store's integration tests, the identity service's
  refresh tests, the startup-validation tests, and a Playwright smoke test.
