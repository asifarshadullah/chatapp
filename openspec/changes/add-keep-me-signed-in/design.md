## Context

See proposal.md — Why. The mechanics that constrain the approach:

- `IdentityService.IssueSessionAsync` stamps `DateTime.UtcNow.Add(_refreshSettings.Lifetime)`
  onto every `RefreshToken` it creates, and `AuthController.IssueSession` stamps the same
  span onto the cookie's `Expires`. Both read one configured value, so today there is
  exactly one possible session length.
- Rotation already recomputes the expiry from "now" for the successor, so sliding renewal
  is what the code would do anyway once the lifetime is per session — the extended window
  falls out of rotation rather than needing a separate mechanism.
- The refresh cookie is `HttpOnly`, so the client cannot inspect it. `authService` tracks
  a `SESSION_KEY` marker in `localStorage` to know whether a refresh is worth attempting.
- Google sign-in leaves the app and comes back through `/auth/callback/google`, so any
  choice made on the form has to survive a redirect the app does not control.

## Goals / Non-Goals

**Goals:**

- One place decides a session's length, and everything downstream — stored expiry, cookie
  expiry, successor expiry — derives from it.
- Existing stored credentials and existing signed-in users keep working across the deploy
  without a data migration step.
- The external-provider path cannot be talked into a longer session than the user asked for.

**Non-Goals:**

- Reworking `TokenDto` into a session-descriptor type. The flag rides alongside the
  existing shape.
- Making the two lifetimes anything other than configuration values.

## Decisions

**The choice lives on the credential, not the user.** `RefreshToken` gains a `bool
Persistent` set at authentication and copied to each successor. Alternatives considered:
storing it on `AppUser` (wrong — it is a property of one device's session, and a user who
opts in on a phone would silently extend their session on a shared desktop) and encoding it
in the raw token value (wrong — the raw value is a random opaque secret and giving it
structure invites parsing it as trusted input). The credential is already the thing whose
lifetime is in question, so it is the thing that should carry the answer.

**Lifetime is resolved once, at issuance.** `IRefreshTokenSettings` grows
`PersistentLifetime` next to the existing `Lifetime`, and `IssueSessionAsync` picks between
them from the session's flag. The chosen span is applied to both the stored `ExpiresAt` and
the cookie's `Expires`, so the cookie can never outlive the credential or vice versa. To
avoid the controller re-deriving the span and drifting from the service, `TokenDto` carries
the refresh credential's absolute expiry alongside the raw value, and the controller sets
the cookie from that — one calculation, two consumers.

**Sliding comes free; the cap is the extended lifetime itself.** A successor's expiry is
`now + lifetime`, so an active user's window keeps moving forward and an abandoned session
still dies 30 days after its last use. No absolute cap from original login is imposed,
because an absolute cap would sign out the app's most active users on a fixed schedule.

It is worth being exact about why that is safe today rather than resting on "rotation and
replay detection are the real guard". They are guards against a credential being *used* by
someone else; they do nothing about a credential simply being *held*. What actually bounds
the risk right now is that this application has no other way to end a session either: there
is no password-change flow, no password reset, no administrative revocation. A cap would
not be closing a gap that anything else closes. The day a password reset ships, that
argument stops holding — resetting a password that leaves every existing session alive is
the wrong behaviour — and this decision must be revisited then.

**Unremembered means a browser-session cookie.** Omitting `Expires` entirely (rather than
setting a one-day expiry) is what makes the choice meaningful on a shared machine. The
server-side one-day expiry stays as the backstop, since a session cookie is a client-side
promise the server should not rely on.

**The local session marker stays in `localStorage`, and records which kind of session it
is.** The first version of this design put an unremembered session's marker in
`sessionStorage`, reasoning that it would then disappear at exactly the moment the cookie
did. That was wrong: `sessionStorage` is scoped to a tab, not to the browsing session, so
opening the app in a second tab found no marker and showed the sign-in form to someone whose
cookie was perfectly good. The marker therefore lives in `localStorage` for both kinds, with
the kind stored beside it — enough for a returning visitor to tell "unremembered, so the
cookie went with the browser" from "remembered, so try renewing" without a request that is
certain to be refused, and shared across tabs the way a session actually is.

**A consumed credential's expiry is pulled in to the replay-detection window.** The TTL index
reaps on `ExpiresAt` plus a retention margin, so with sliding 30-day lifetimes a credential
consumed six minutes after it was issued would otherwise sit in the collection for over a
month. Consumption is what makes it dead; the only reason to keep the record at all is so a
replay of it is recognised rather than looking merely unknown. Consuming therefore rewrites
`ExpiresAt` to now plus that window, and storage grows with the number of sessions rather
than with how long they last.

**The OAuth choice travels in `AuthenticationProperties.Items`.** ASP.NET Core round-trips
those items through the provider inside its own encrypted, signed state, so the callback
reads back exactly what the challenge wrote and a crafted callback URL cannot forge a
longer session. Alternatives considered: a query parameter on the callback (forgeable by
anyone who can hand the user a link) and a separate cookie set before the challenge
(another cookie to scope, expire, and get wrong under `SameSite`). Anything unreadable or
absent resolves to the ordinary session.

**Absent means false, everywhere.** `RefreshTokenDocument` deserializes a missing
`Persistent` field as `false`, so credentials issued before the deploy keep the one-day
lifetime they were issued under and no backfill job is needed. Likewise a request body
without the flag is an ordinary login, which keeps the API compatible with any client that
has not been updated.

## Risks / Trade-offs

- **A 30-day credential is 30 days of exposure if stolen** → Nothing else about the
  credential relaxes: it stays `HttpOnly`, hashed at rest, rotated on every use, and a
  replay still revokes the family. The exposure window lengthens; the detection does not
  weaken. This is the trade the user is being asked to make, which is why the choice is
  theirs and defaults to off.
- **A remembered session would outlive a password change** → There is no password-change or
  password-reset flow in this codebase; `SetPasswordHash` is called only at registration. So
  this is not a live gap, but it is a trap laid for whoever adds one: a reset that leaves
  30-day sessions running is worse than useless. Revoking a user's families on credential
  change belongs in the same change as the reset flow, not after it.

- **Several tabs renewing at once look like a replay** → Tabs of one session share the
  cookie but not the in-flight renewal, which is per page context. Two tabs finding the
  access token stale together will both present the same credential, and the second one
  reads as a replay: the family is revoked and the user is signed out everywhere. This
  predates the change, but a 30-day session makes it far likelier, because tabs stay open
  for weeks and every wake-from-sleep is a synchronised renewal across all of them. The
  spec now requires that this not end the session; the mechanism — a cross-tab lock, or a
  server-side grace window returning the successor to a just-consumed credential — is
  deliberately left to a follow-up rather than rushed into this change.

- **A stolen device holds a self-renewing credential for a month, and the user cannot stop
  it** → With no password reset, no device list and no revocation UI, the only thing that
  ends a remembered session is signing out on the device itself — which is precisely what
  the person who lost it cannot do. At a one-day lifetime this was a day's exposure and
  barely worth naming; at 30 days it is the largest single cost this change incurs.
  Recorded here rather than solved, so that whoever builds revocation or device management
  sees the bill this change ran up instead of inheriting it silently.
- **Users lose sessions on deploy of the unremembered path** → Existing cookies keep their
  one-day expiry until they rotate; the browser-session behaviour begins at the next
  sign-in. No one is signed out by the deploy itself.
- **Two lifetimes to configure means two ways to misconfigure** → Both are bound from the
  same `RefreshToken` section with defaults in code, and the extended lifetime being
  shorter than the ordinary one is a configuration error worth failing loudly at startup.
