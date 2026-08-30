## Why

A session that ends by itself is treated exactly like a user who chose to sign out, and one of
those is destructive. `App.endSession` serves both, and it calls `authService.logout()` — a
POST to `/auth/logout` that revokes the refresh credential's whole family server-side.

A spike against the real service confirmed the cost. Concurrent renewal leaves two live
credentials in one family, signing out with either revokes both, and signing out with the
already-spent original does the same. So the client's reflex on an expiry can destroy a
credential that was still good.

The reachable path is the one that never touches the server: `getValidToken` raises the
session-ended signal locally when it finds no session marker, and at that moment the http-only
refresh cookie may be perfectly exchangeable. The client then throws it away on the user's
behalf, turning a recoverable session into a mandatory sign-in.

## What Changes

- A session that ends on its own is discarded locally: the stored token, its expiry, the
  session marker and the companion cookie, plus the hub connection. No `/auth/logout` request
  is made, because the user did not ask to sign out and there may be nothing wrong with the
  credential.
- A deliberate sign-out is unchanged: it still revokes server-side, which is the whole point
  of it.
- The two are separate operations at the call site rather than one operation serving both
  intentions, so a future caller has to choose which it means.

## Capabilities

### New Capabilities

None. This adds a client obligation to a capability that already describes sign-out.

### Modified Capabilities

- `identity/token-refresh`: gains a requirement that a session ending by itself is discarded
  locally and does not invoke sign-out. The existing sign-out requirement is untouched and
  stays exactly true — it describes what the operation does, not who may invoke it.

## Non-goals

- **No server change.** Family-wide revocation is correct for a deliberate sign-out and is the
  same rule replay detection depends on. Narrowing it would mean a user signing out during a
  renewal race leaves a live credential behind, which is the opposite of what sign-out
  promises.
- **No recovery semantics.** The abandoned session clears its marker like any other, so the
  next load shows sign-in rather than attempting to restore against the surviving cookie.
  Attempting recovery is attractive — the cookie may well be good — but it is a different
  change, and it risks a restore-fail-bounce loop.
- **The orphaned credential is accepted.** It stays exchangeable until its own lifetime
  elapses and the reaper removes it. It is http-only and sits in the browser that just failed
  to use it; an expiry is not evidence of compromise, and revoking buys nothing an attacker
  holding the cookie could not already do.

## Impact

Identity context, client side only — `frontend/chat-ui/src/App.tsx`, which owns both
handlers today. `authService.clearLocal` and `signalRService.stop` already exist and are
public; nothing new is needed from either service.

No backend change, no API change, no spec change to sign-out itself. Verified by Vitest for
the request that must not happen, and by one Playwright test that forces an expiry against the
real backend and shows the refresh credential still exchanges afterwards.
