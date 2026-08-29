## Why

A user with two tabs open can be signed out of both by a renewal that only one of them
needed. The access token lives in shared browser storage, but no tab watches it, so a tab
whose token has gone stale renews without noticing that a sibling has already fetched a fresh
one — and when that unnecessary exchange is refused, the refusal discards the session for
every tab, including the ones holding a perfectly good token.

`survive-concurrent-renewal` fixed the server half, and in doing so it narrowed this
considerably: overlapping exchanges now succeed within a two-second grace window, so the
refusal that used to end the session mostly does not happen. What remains is the case where a
redundant exchange arrives more than two seconds late — a tab throttled in the background, a
machine resuming from sleep, a slow network. Rarer than issue #5 was written to describe, but
the failure is still a silent sign-out of every open tab, and the blanket clear that causes it
is still there.

This is therefore a robustness change rather than a live bug fix. It is worth making because
the client currently ends a session it has positive evidence is alive.

## What Changes

- A refused renewal no longer discards the session outright. The client first establishes
  whether a sibling stored a different token while its own exchange was in flight; if one did,
  the refusal concerned a credential that has since been superseded, and the session
  continues. Only a refusal with nothing stored behind it ends the session. **This is what
  closes issue #5.**
- A client that finds its access token stale re-reads the shared store before renewing, and
  uses a token a sibling has already obtained instead of exchanging again. This does not fix
  the sign-out — in the failing case the sibling has not stored anything yet — but it reduces
  how often clients renew redundantly, and so how much the server's grace window has to carry.
- A client that cannot renew, but has evidence the session is alive, reports a transient
  failure rather than an ended session. The existing session-ended signal causes a
  server-side revocation, so a client without a usable token must be able to say "not now"
  without saying "over".

## Capabilities

### New Capabilities

None. This constrains behaviour the `identity/token-refresh` capability already describes.

### Modified Capabilities

- `identity/token-refresh`: gains two requirements covering the client's side of the
  concurrent-renewal guarantee — that clients of one session share renewals rather than
  duplicating them, and that one client's refusal does not end the session for the others. The
  existing replay requirement is amended so its "must authenticate again" scenario does not
  contradict them.

## Non-goals

- **No `storage` event listener.** Reacting to a sibling's writes as they happen is real
  cross-tab coordination, which this client does not have and does not need here: the event
  does not fire in the tab that wrote, so the re-read is required regardless. Worth revisiting
  when a tab must react to a sibling's *sign-out*, which is a different problem.
- **No change to the server.** Exchange, rotation, replay detection and the grace window stay
  as `survive-concurrent-renewal` left them.
- **No new storage.** No lock, no election, no coordination channel between tabs. Every
  decision is made from the shared values already written.
- **Not closing the overlap entirely.** Two tabs that go stale at the same instant, with
  nothing yet stored by either, still both exchange. That is the case the grace window exists
  to absorb.
- **Not changing what a genuinely ended session does.** `endSession` revokes the credential
  family server-side on any session-ended signal, so one tab's ordinary expiry currently ends
  the session for every other tab. That is the same class of defect as #5 and is filed
  separately; this change only avoids making the refusal path reach it.
- **Issues #2, #3 and #4 stay open.**

## Impact

Identity context, client side only — `frontend/chat-ui/src/services/authService.ts`, which
owns renewal; `authorizedFetch.ts`, which retries through it; and `sessionErrors.ts`, which
gains a non-fatal renewal failure alongside the existing session-ended error. `signalRService`
and `ChatWindow` already route unrecognised errors to a transient message without ending the
session, so they need no change — a property worth keeping, and worth a test.

No backend project changes; no API surface changes. Verified by Vitest, with the sibling tab
represented by a write to shared storage between an exchange being issued and its outcome
arriving.
