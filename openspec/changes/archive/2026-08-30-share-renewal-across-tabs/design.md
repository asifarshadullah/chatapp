## Context

See proposal.md — Why.

Everything here lives in the Identity context's client side:
`frontend/chat-ui/src/services/authService.ts`, which owns the access token, the session
marker and renewal; `authorizedFetch.ts`, which renews on a 401; and `sessionErrors.ts`, which
defines what an ended session looks like to callers. `signalRService` renews only through
`getValidToken` and so needs no change.

Four properties of the existing code shape the approach:

- The access token and its expiry are held in `localStorage`, shared by every tab of the
  origin. `_store` writes them; nothing reads them back except at the start of an operation.
- `_refreshPromise` already collapses concurrent callers, but only *within* one tab. It is an
  instance field, and each tab has its own instance.
- `refresh()` calls `clearLocal()` on any non-OK response. That is the blanket clear the
  change has to narrow.
- **`SessionExpiredError` is not merely a local signal.** It reaches `App.endSession`, which
  calls `authService.logout()` — a POST to `/auth/logout` that revokes the credential family
  server-side. Raising it is therefore an act, not a report: it ends the session for every
  client, irrevocably. This constrains the whole design and is the reason for the new error
  type below.

No type crosses a layer boundary and no new interface is introduced: this is behaviour added
to a service that already exists, in the layer it already occupies, plus one error type beside
the one it belongs with.

## Goals / Non-Goals

**Goals:**

- Make the shared store the authority on the current token, consulted at the two moments the
  answer can have changed underneath a tab: before an exchange, and after one is refused.
- Never end a session the client has positive evidence is alive — including not ending it by
  raising an error whose handling revokes it.
- Keep the narrowing conservative: a refusal ends the session unless there is evidence to the
  contrary.

**Non-Goals:**

- The proposal's non-goals carry over unchanged.
- Not fixing `endSession`'s server-side revocation on ordinary expiry. Filed separately; this
  change only avoids routing the refusal path into it.

## Decisions

### Two predicates, not one: evidence versus usability

The natural implementation asks one question — "is there a usable token in storage?" — and
uses the answer for both the reuse check and the refusal check. That conflates two different
questions, and the conflation signs users out.

On the refusal path the question is *did a sibling exchange successfully*, and any different
stored token answers it, including one already inside the renewal margin. Requiring usability
there would discard a session that is demonstrably alive, merely because the sibling's token
happened to be sixty seconds from expiry.

On the reuse path the question is *can I proceed without exchanging*, and a token near expiry
is no good: adopting it means renewing again moments later.

So: **evidence** is `present && different`; **usable** is `evidence && !stale`.

### Difference is judged by token identity, never by expiry

The reuse check must compare against a token known to be unusable, not against "what is
stored". On the path `authorizedFetch` takes, the server has just rejected a token that *is*
stored and *does* look unexpired; re-reading storage returns that same token, and the retry
sends the rejected token a second time. Today's unconditional renewal on a 401 is correct for
exactly that reason.

`refresh()` therefore takes the superseded token as an optional argument, defaulting to
whatever is in storage at entry, which keeps existing no-argument callers (`restoreSession`)
behaving as they do now and makes the existing test suite the compatibility guard.

`authorizedFetch` is the only caller for which the argument changes behaviour: there the
rejected token is what is stored, so without naming it the retry would resend it. The reuse
check cannot fire for `getValidToken` at all — it reads storage itself, synchronously, so a
sibling's fresh token is returned by its own early exit and a stale one leaves nothing to
differ from. It passes the token it read regardless, as a statement of intent that stays
correct if anything is ever awaited between the read and the renewal, but nothing observable
depends on it and no test should pretend otherwise.

Alternative considered: comparing expiry timestamps. Rejected because expiry cannot
distinguish "superseded" from "repudiated" — which is the 401 case above.

### Absence is not evidence

A stored token that has been *removed* is also "different" from the one we set out with, but
it means the opposite: another client signed out and cleared the store. Evidence requires a
present token. Without this guard, signing out in one tab would leave the others believing the
session was alive.

### A refusal with evidence but no usable token raises a new, non-fatal error

Given that `SessionExpiredError` revokes the family server-side, a tab that cannot renew but
has evidence the session lives must not raise it — doing so would be strictly worse than the
bug being fixed, killing every sibling permanently rather than merely signing them out.

`RenewalFailedError` joins `SessionExpiredError` in `sessionErrors.ts`. Both existing
consumers already route unrecognised errors to `CONNECT_FAILED_MESSAGE` without calling
`onSessionExpired`, so the new type is safe with **zero consumer changes** — a property that
is load-bearing and therefore gets its own test rather than being left to chance. The message
is slightly untrue (the server was reached) and that is accepted: the state is transient and
self-correcting on the next interaction, and the alternative means editing the sign-out branch,
which is the code most dangerous to disturb here.

### `_refreshPromise` stays as it is

Correct for what it does — collapsing callers inside one tab — and the change adds the
cross-tab counterpart alongside it. Reworking it into a cross-tab lock would mean the
coordination this change declines to introduce.

### The sibling tab is a `localStorage` write, not a second browser context

Under Vitest with a jsdom `localStorage`, "another tab renewed" is exactly a write to the
shared store between the fetch being issued and its outcome arriving — reachable from a mocked
`fetch`. No Playwright context, no `BroadcastChannel`, no fake tabs. This is why the change is
testable at the unit level despite being about cross-tab behaviour, and why the two-tab check
stays a manual confirmation rather than an automated one: Playwright can open two pages but
cannot reliably arrange a refusal more than two seconds late.

## Risks / Trade-offs

- **A tab continues on a sibling's token after a refusal that was genuine** → Bounded by the
  access token's lifetime (`Jwt.ExpiryMinutes`, currently seven minutes). The sibling's token
  was issued by a successful exchange, so the session was live moments earlier; when it lapses,
  the next renewal has no evidence behind it and the session ends. A revoked bearer token is
  already valid until expiry by design, and every sibling coasts on the same grace. If
  `ExpiryMinutes` is ever lengthened for unrelated reasons, this window lengthens with it.
- **Reuse hides a real problem** → A tab that keeps finding a sibling's token never exchanges
  and never notices the credential is gone. Bounded the same way: reuse requires a token that
  is not stale, and tokens stop being usable.
- **`localStorage` is not transactional** → Two tabs can interleave writes between a read and
  the decision that follows. Every decision here is monotonic — use a newer token, or do not —
  so an interleaving costs at most one redundant exchange, which the server tolerates.
- **The change is invisible in a single tab** → All of it is dormant with one tab open, so a
  regression would be caught only by tests that stage a sibling write. Those tests are the
  guard; they belong with the change, not after it.
- **`RenewalFailedError` could later be handled as fatal** → A future consumer that treats
  unknown errors as session-ended would silently reintroduce the revocation this avoids. The
  test that both consumers leave the session intact is what makes that regression visible.
