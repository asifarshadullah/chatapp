## Context

See proposal.md — Why. The mechanics that matter:

- `IdentityService.RefreshAsync` finds the credential by hash, and if `ConsumedAt is not null`
  revokes the family and refuses. That branch is the visible defect.
- `IRefreshTokenStore.UpdateAsync` is a whole-document `ReplaceOneAsync` with no guard, so two
  overlapping exchanges both read the credential as unconsumed and both write. The second
  write erases the first. That lost update is the invisible defect, and it currently hides the
  visible one in the simultaneous ordering.
- `RefreshToken.Consume` pulls `ExpiresAt` in to `now` so the TTL index can reap consumed
  records promptly. That destroys the one fact needed to bound a grace-issued credential, so
  this change has to preserve it before overwriting it.
- Only a hash of each credential is stored, so the successor's raw value is unrecoverable.
  This forces the sibling shape rather than "return the successor".
- The credential lives in one HttpOnly cookie jar shared by every client. That is why the
  collision is confined to in-flight overlap, and why the sibling shape converges.
- Consumed records survive seven days past expiry via the TTL index, so a two-second grace
  window needs no change to retention.
- There is no `ILogger` anywhere in `backend/src`. This introduces the first.

## Goals / Non-Goals

**Goals**
- Make consumption a decision the store can arbitrate, so "did this exchange consume the
  credential" has one answer rather than depending on write order.
- Keep every lifecycle predicate on the entity, where `IsUsable`, `Consume` and `Revoke`
  already live, so they are unit-testable without a store.
- Leave the single opaque refusal intact: nothing here may become a way to probe which
  credentials exist.

**Non-Goals**
- Changing how the client renews. A grace exchange is indistinguishable from an ordinary one
  from outside.
- Reworking retention. The TTL index and its seven-day margin stand.

## Decisions

### Conditional consume on the store (Application defines, Infrastructure implements)

`IRefreshTokenStore` gains `Task<bool> TryConsumeAsync(RefreshToken token, DateTime now,
CancellationToken ct)`, returning whether this caller consumed it.
`MongoRefreshTokenStore` implements it as an `UpdateOneAsync` filtered on the id **and**
`ConsumedAt == null`, setting `ConsumedAt` and the pulled-in `ExpiresAt` in one operation, and
returning `ModifiedCount == 1`.

Application defines it because the identity service needs it; Infrastructure implements it
because only Mongo knows how to express the condition. `UpdateAsync` stays on the interface —
tests use it to arrange state — but the refresh path stops calling it.

*Alternative rejected:* `FindOneAndUpdate` returning the pre-image. It would save the re-read
on the losing path, but it returns a document rather than a boolean and pushes document
mapping into a decision the service should be making. The re-read costs one indexed lookup on
a path that is already rare.

*Alternative rejected:* leaving the unguarded replace and relying on the grace window. The
lost update makes overlapping exchanges succeed by accident rather than by rule, which is
fine until someone adds a retry.

### A session ceiling, set on the grace path and inherited by every successor

Two fields, both on `RefreshToken` (Domain), both facts about one credential's own lifecycle:

- `PreConsumptionExpiresAt`, set by `Consume` to the value `ExpiresAt` held before it was
  pulled in. This is how far the presented credential itself would have reached.
- `SessionExpiresAt`, a nullable ceiling. Set when a credential is issued on the grace path, to
  the nearer of the predecessor's `PreConsumptionExpiresAt` and any ceiling the family already
  carried, and **inherited by every successor thereafter, ordinary rotations included**.

A session that never renews under grace never acquires a ceiling, so ordinary sliding renewal
is untouched and continued use still keeps a session alive indefinitely — the requirement the
previous change established.

*The design this replaced, and why.* The first draft had only the first field: cap the
grace-issued credential at its predecessor's pre-consumption expiry, and skip propagation on
the grounds that the cap binds grace-issued credentials only. That accounted for how a ceiling
is created and not for how it must be inherited, and it was wrong. A grace-issued credential is
otherwise unremarkable, so the very next renewal takes the ordinary path and slides the session
back to a full lifetime: the bound survived exactly one exchange, and a replayer escaped it for
the price of one more request. Task 5.4 caught it. Inheritance is the part that does the work.

Records written before this change deserialize both fields as null, read as "no ceiling known".

### The grace predicate on `RefreshToken` (Domain)

`bool IsWithinGrace(DateTime now, TimeSpan grace)` — true when the credential was consumed,
is not revoked, and `now - ConsumedAt <= grace`. Domain, same shape as `IsUsable`. The
duration is policy and is passed in, so Domain keeps its zero dependencies.

It deliberately does **not** consult `ExpiresAt`: `Consume` has already pulled that to the
moment of consumption, so testing it would reject every grace exchange. The session's real
lifetime is enforced by the credential having been usable when it was consumed, and by the cap
above.

### Ordering inside `RefreshAsync`

```
find by hash; refuse if unknown
move the user lookup above the consumed branch   (the grace path needs the user)

if not stored.ConsumedAt is null or not IsUsable:  handled by the paths below

consumed := await TryConsumeAsync(stored, now)
if consumed:
    ordinary rotation, unchanged
else:
    re-read by hash
    if IsWithinGrace(now, settings.GraceWindow):
        log warning with FamilyId
        issue a sibling in the same family, capped at PreConsumptionExpiresAt
    else:
        revoke the family; refuse
```

A credential that was already consumed when first read takes the same path: `TryConsumeAsync`
returns false and the grace check decides. There is one rule, reached two ways, which is what
the spec requires — losing a race and replaying an old credential are judged identically.

Note what this does **not** do: it never re-consumes. `ConsumedAt` keeps its original value, so
the grace window is anchored to the first exchange and cannot be walked forward by repeated
presentation.

### Grace window: two seconds, on `IRefreshTokenSettings` (Application)

`TimeSpan GraceWindow { get; }` joins `Lifetime` and `PersistentLifetime`;
`RefreshTokenSettings` binds `RefreshToken:GraceWindowSeconds`, defaulting to 2. `Program.cs`
validates it as positive and shorter than `Lifetime`.

Two seconds because the phenomenon is in-flight request overlap — the gap between a request
being sent and another response updating the shared cookie jar. That is milliseconds; two
seconds absorbs server queueing, a GC pause, or a slow round trip with three orders of
magnitude to spare. The thirty seconds first proposed came from a mental model of tabs holding
stale credentials for weeks, which the shared cookie jar makes impossible.

### Logging via `ILogger<IdentityService>`

Already available through the `Microsoft.AspNetCore.App` framework reference; no new package,
no new interface. Warning level, because the signal preserved is a security one: a real replay
attack now appears as repeated grace hits on one family and must clear a default log level.

*Alternative rejected:* an Application-defined `IAuditLog`. Purer under the inward-dependency
rule and the right move as soon as a second call site appears; for one line it is indirection
with nothing behind it. Recorded so the shortcut is a decision, not an oversight.

## Risks / Trade-offs

**Replay detection is genuinely weakened for two seconds.** → An attacker who captures a
credential and presents it inside the window gets a working session, and because the
legitimate holder is not revoked, gets it *silently*. This is the real cost. Mitigations: the
window is seconds; the attacker must already be positioned to capture the credential at the
moment of use; the credential they receive is capped at the session's remaining life rather
than a fresh thirty days; and the exchange is logged. Accepted because being signed out during
ordinary multi-tab use happens to real users, while this attack requires an adversary who at
that point has better options.

**A family may hold more than one active chain.** → Each grace exchange leaves a sibling the
browser did not keep, still valid until the cap. Harmless now — it is never presented, and the
TTL reaps it — but "one family, one chain" stops being an invariant. Anything built later on
families must not assume it. Issue #3, per-session revocation, is the likely next work and is
the reason this is flagged rather than buried.

**Grace could mask a client bug that renews needlessly.** → The log line is the mitigation,
and is why logging is in scope rather than deferred.

**The user-visible symptom does not fully disappear.** → The client still clears shared
`localStorage` on any refused renewal, signing out every tab, and still does not re-read a
token another tab just fetched. Deliberately out of scope and tracked separately, but it means
"fixed" here means the server no longer causes it — not that no one will see it again.

**The new fields are absent on existing records.** → Treated as "no ceiling known", so a grace
exchange against a credential issued before deployment yields an unbounded one. The exposure
lasts only while pre-deployment credentials remain unconsumed — at most the ordinary lifetime
for most sessions — and closing it properly would need a backfill for a window that closes on
its own.

## Migration Plan

A plain rolling deploy. Nothing changes shape destructively, no index moves, and old and new
instances disagree only on whether a two-second-old consumed credential is honoured — which is
the fix, and is safe in either direction. Rollback is reverting the deployment; the defect
returns and no data is left inconsistent, since the new field is simply ignored.

`RefreshToken:GraceWindowSeconds` is absent from existing configuration and takes its default.

## Open Questions

None. Every decision that would change the specs, the approach or the task breakdown was
settled with the user before this document was written.
