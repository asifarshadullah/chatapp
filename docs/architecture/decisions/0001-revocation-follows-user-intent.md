# Revoke a credential family on user intent, not on circumstance

- Status: accepted
- Date: 2026-08-30

## Context and Problem Statement

Refresh credentials are organised into families: successive credentials issued from one
authentication share a family, and revoking one revokes them all. Family-wide revocation is
what makes replay detection meaningful — a captured credential must take the whole session
down, or the attacker keeps it.

Revocation is exposed as a single server operation, invoked by the client, that revokes the
family of whatever credential is presented. Nothing about that operation says when it is
appropriate to call, and the client called it in two quite different situations: when the user
chose to sign out, and when the client discovered it could no longer obtain an access token.

Those situations are not equivalent, and the difference is expensive. A family can hold more
than one live credential, because concurrent renewal within the grace window issues a second
credential in the same family. Revocation from a credential the server considers spent still
takes down the live ones. And a client can conclude its session has ended without contacting
the server at all, at a moment when its refresh credential is perfectly exchangeable — so the
reflex to revoke destroys a session that could have continued.

The question this settles: what licenses a client to revoke?

## Considered Options

- **Revocation follows user intent.** Only a deliberate sign-out revokes. A session that ends
  by itself is discarded locally, and the credential is left to lapse.
- **Make the server tolerant.** Keep the client revoking whenever a session ends, and narrow
  the server so that a spent or superseded credential revokes less than the whole family.
- **Leave both as they are.** Treat the destroyed credential as acceptable, on the grounds
  that a session which cannot renew is over anyway.

## Decision Outcome

Chosen option: **revocation follows user intent**, because intent is known only at the client,
and the server cannot recover it from what it is presented. A credential arrives without any
indication of whether the person meant to end their session or merely closed a laptop, so
asking the server to distinguish the two means asking it to guess.

Making the server tolerant was rejected for a second reason: it weakens sign-out itself. A
user signing out during a renewal race would leave a live credential behind, which is exactly
what sign-out promises not to do. Leaving both as they are was rejected because the premise is
false — a client's inability to renew does not establish that the credential is dead, and in
the local-discovery case it usually is not.

This governs any future capability that ends sessions: password reset revoking existing
sessions, and revoking a session on a lost device, both act on a user's intent and should
revoke. Session lifetime elapsing, a failed renewal, and a client giving up should not.

### Consequences

- Good, because revocation now means something: it is an act the user asked for, not a side
  effect of circumstance. A recoverable session is no longer destroyed by a client that could
  not see it was recoverable.
- Good, because the server's family-wide rule stays intact, and with it replay detection.
- Bad, because a credential outlives the client that abandoned it, remaining exchangeable
  until its own lifetime elapses — up to a day for a remembered session. This is bounded by
  the existing reaping of unusable credentials, and the credential is inaccessible to script,
  held only by the browser that just failed to use it.
- Bad, because the rule lives in the client and cannot be enforced by the server. A future
  caller can still invoke sign-out for the wrong reason; only naming and tests guard it.
- Follow-up: issues #2 and #3 build revocation features and should cite this record rather
  than re-deciding the question.
