# ADR Review Manifest

- Status: completed
- Review date: 2026-08-30

## Review Summary

ADR review completed for this change.

The change turns on a question broader than its own diff: what licenses a client to revoke a
credential family. The answer governs the two revocation features already on the backlog —
password reset revoking existing sessions (#2) and revoking a session on a lost device (#3) —
so it is recorded durably rather than left in this change's design, which is archived once the
work lands.

Every decision in `design.md` was accounted for and put to the user in one round. Two were put
forward as candidates and one was promoted.

Considered and declined:

- **The orphaned credential is accepted rather than reaped eagerly** — offered as a candidate,
  since it is a security trade-off a later change might reverse while believing it was tidying
  up a loose end. Declined as a separate record: it is a consequence of the promoted decision
  rather than an independent commitment, and ADR-0001 already carries it under Consequences,
  which is where a reader would look.
- **Two named functions rather than a boolean flag** — assessed tactical. A naming shape that
  binds nothing beyond this change.
- **Abandonment clears the session marker along with the token** — assessed tactical. A scope
  choice for this change; whether an abandoned session should later attempt recovery is left
  open in the proposal's Non-goals.

## In-Force ADRs Reviewed

- None — `docs/architecture/decisions/` did not exist before this change, so there were no
  in-force ADRs and no supersession graph to walk. `docs/architecture/trade-offs/` was read as
  context; it holds pre-OpenSpec decisions and is closed to new entries.

## New Durable ADRs Created

- `docs/architecture/decisions/0001-revocation-follows-user-intent.md` — establishes that only
  a deliberate sign-out revokes a credential family, and that a session ending by itself is
  discarded locally.
