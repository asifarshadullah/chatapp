## Why

Refresh tokens live one day and access tokens seven minutes, so anyone who closes
the browser for a weekend comes back to the sign-in form. That lifetime is right
for a shared machine and wrong for the phone or laptop a user owns; the choice
belongs to the user, not to a single global setting.

## What Changes

- Add a "Keep me signed in" choice to the sign-in and registration forms, and
  carry it through the Google sign-in flow so the checkbox means the same thing
  whichever button the user presses.
- When the user opts in, the session's refresh credential lives 30 days instead
  of one, and each rotation extends the window from the moment of use, so a user
  who keeps using the app is never signed out for elapsed time alone.
- When the user does not opt in, the credential keeps its one-day lifetime and
  its cookie becomes a browser-session cookie, so closing the browser ends the
  session. **BREAKING** for the un-remembered path: today a session survives a
  browser restart for up to a day; after this change it does not.
- The opt-in is a property of the session, carried by the credential family
  rather than by the user record, so it survives rotation without being asked
  again and does not leak between a user's sessions on different devices.

This change belongs to the Identity bounded context: it concerns how long an
authenticated session may be continued, which is `Chat.Identity.*` and the auth
surface in `Chat.Api`, plus the sign-in UI. Chat and Billing are untouched.

## Non-goals

- No per-device session list or "sign out everywhere" screen. Revocation stays
  as it is today: sign-out and replay detection revoke a family. Note what that
  costs at 30 days — a user who loses a device has no way to end the session on
  it, since this codebase has no password reset either. That is accepted here
  and recorded in design.md, not solved.
- No idle timeout distinct from the credential lifetime, and no re-authentication
  prompt before sensitive actions. A 30-day remembered session is a deliberate
  trade, not something to claw back with a second mechanism.
- No change to the access token's seven-minute lifetime, to rotation, or to
  replay detection. Remembering a user lengthens the window, it does not weaken
  what guards it.
- No "remember this device" recognition, trusted-device fingerprinting, or
  second factor.

- No fix for concurrent renewal across browser tabs. Two tabs of one session
  renewing at the same moment still look like a replay and still end the
  session. This predates the change but a 30-day session makes it far likelier,
  so it is named here rather than left to be rediscovered. It needs a change of
  its own; deliberately not specified here, because a requirement the code does
  not meet is worse than a gap that is written down.

## Capabilities

### New Capabilities

None. This extends an existing capability rather than introducing one.

### Modified Capabilities

- `identity/token-refresh`: the refresh credential's lifetime becomes a property
  of the session chosen at authentication rather than a single configured value;
  the un-remembered cookie becomes a browser-session cookie; a remembered
  session's window extends on each rotation; and the client sends and remembers
  the user's choice, including across the external-provider redirect.

## Impact

- `Chat.Identity.Domain`: `RefreshToken` carries the session's persistence
  choice so a successor can inherit it.
- `Chat.Identity.Application`: `IIdentityService.RegisterAsync`, `LoginAsync`
  and `HandleExternalCallbackAsync` accept the choice; `IRefreshTokenSettings`
  grows a remembered lifetime alongside the existing one.
- `Chat.Identity.Infrastructure`: `IdentityService` issuance, the Mongo
  refresh-token document and store round-trip the new field; existing stored
  documents lacking it read as not-remembered.
- `Chat.Api`: `LoginDto`/`RegisterDto` gain the flag, the refresh cookie's
  `Expires` is chosen per session, and the Google challenge carries the choice
  through to its callback.
- `frontend/chat-ui`: the auth forms gain the control and `authService` sends it;
  the local session marker records which kind of session it is, so a second tab
  and a returning visitor are told apart; the sign-in path is covered by
  `e2e/playwright`.
- Retention: consuming a credential pulls its stored expiry in to the
  replay-detection window, so the existing TTL index reaps consumed credentials
  without waiting out the session they belonged to.
- Configuration: `RefreshToken` section in `appsettings*.json` gains the
  remembered lifetime.
