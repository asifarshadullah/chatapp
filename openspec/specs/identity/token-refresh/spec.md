# Token Refresh Specification

## Purpose

Keeps a signed-in session usable beyond the access token's lifetime by exchanging a
long-lived, revocable refresh credential for fresh access tokens, while ensuring a stolen
refresh credential is detected on replay and ends the session rather than granting an
attacker an indefinite one.

## Requirements

### Requirement: Refresh credential issued on authentication

The system SHALL issue a refresh credential whenever it issues an access token through
registration, password login, or external provider callback. The refresh credential SHALL be
delivered only as a cookie that client scripts cannot read, and SHALL never appear in a
response body or URL. The cookie SHALL be retained across browser restarts only when the
user chose to stay signed in, and SHALL then be retained no longer than the credential
itself remains exchangeable.

#### Scenario: Registration issues a refresh credential

- **WHEN** a visitor registers with a valid email, password, and display name
- **THEN** the response returns an access token and its expiry
- **AND** a refresh credential is set as a cookie marked to be inaccessible to scripts, sent
  only over a secure connection outside development, and restricted from cross-site sending

#### Scenario: Password login issues a refresh credential

- **WHEN** a registered user signs in with correct credentials
- **THEN** the response returns an access token and a refresh credential cookie

#### Scenario: External provider login issues a refresh credential

- **WHEN** a user completes a Google sign-in and the provider callback succeeds
- **THEN** the response returns an access token and a refresh credential cookie

#### Scenario: The refresh credential is never exposed to scripts

- **WHEN** any authentication response is returned
- **THEN** the response body contains no refresh credential
- **AND** the refresh credential cannot be read by client-side script

#### Scenario: A remembered credential is retained for its lifetime

- **WHEN** a session is issued to a user who chose to stay signed in
- **THEN** the cookie is retained until the credential ceases to be exchangeable

#### Scenario: An unremembered credential is retained only for the browsing session

- **WHEN** a session is issued to a user who did not choose to stay signed in
- **THEN** the cookie is retained only for the browsing session

### Requirement: Exchanging a refresh credential for a new access token

The system SHALL accept a refresh credential and return a new access token when that
credential is valid, unexpired, unconsumed, and belongs to a family that has not been
revoked. It SHALL reject the exchange otherwise, and SHALL NOT reveal which condition failed.

#### Scenario: Valid credential is exchanged

- **WHEN** a client presents a valid, unconsumed, unexpired refresh credential
- **THEN** a new access token and its expiry are returned
- **AND** the identified user is the one the credential was issued to

#### Scenario: Expired credential is rejected

- **WHEN** a client presents a refresh credential whose lifetime has elapsed
- **THEN** the exchange is refused and no access token is returned

#### Scenario: Unrecognised credential is rejected

- **WHEN** a client presents a refresh credential that was never issued
- **THEN** the exchange is refused and no access token is returned

#### Scenario: Missing credential is rejected

- **WHEN** a client requests an exchange without presenting a refresh credential
- **THEN** the exchange is refused and no access token is returned

#### Scenario: Rejection does not disclose the reason

- **WHEN** an exchange is refused for any reason
- **THEN** the response distinguishes only that it was refused, not which condition failed

### Requirement: Rotation on every exchange

The system SHALL consume the presented refresh credential on every successful exchange and
issue a distinct successor credential in its place. Consumption SHALL take effect only while
the credential is unconsumed, so that two overlapping exchanges cannot both consume it. A
consumed credential SHALL NOT be exchangeable again once its grace window has passed. The
successor SHALL inherit the session's chosen length, and its own lifetime SHALL be measured
from the moment it is issued — so a session in continued use is never ended by elapsed time
alone.

#### Scenario: Successful exchange rotates the credential

- **WHEN** a valid refresh credential is exchanged
- **THEN** a successor credential is issued that differs from the one presented
- **AND** the successor is delivered by the same cookie mechanism

#### Scenario: The successor is usable

- **WHEN** a successor credential from a previous exchange is presented
- **THEN** the exchange succeeds and issues a further successor

#### Scenario: A consumed credential cannot be reused

- **WHEN** a credential that has already been exchanged is presented again after its grace
  window has passed
- **THEN** the exchange is refused

#### Scenario: Consumption is not overwritten by a later exchange

- **WHEN** an exchange attempts to consume a credential that another exchange has already
  consumed
- **THEN** the earlier consumption stands unchanged
- **AND** the later exchange is resolved by the replay rules rather than by consuming it

#### Scenario: The successor inherits the session's length

- **WHEN** an extended session's credential is exchanged
- **THEN** the successor is exchangeable for the extended period, not the ordinary one

#### Scenario: Continued use keeps a session alive indefinitely

- **WHEN** a user of an extended session returns and exchanges within the extended period,
  repeatedly, over a span longer than that period
- **THEN** each exchange succeeds
- **AND** the user is not asked to sign in again

#### Scenario: An abandoned session still ends

- **WHEN** an extended session is not exchanged for longer than the extended period
- **THEN** the next exchange is refused and the user must sign in again

### Requirement: Replay of a consumed credential revokes the session family

Successive credentials issued from one authentication SHALL form a family. A legitimate
client discards each credential as it is consumed, so presenting a consumed credential once
its grace window has passed indicates the credential was captured and replayed. The system
SHALL treat that as a compromise and revoke every credential in that family, requiring the
user to authenticate again.

Whether an exchange is a replay SHALL be decided solely by how long ago the credential was
consumed. It SHALL NOT depend on how many times the credential has been presented, on any
property of the caller, or on whether the exchange won or lost a race to consume it: the
system cannot tell the legitimate holder from an attacker, and guessing would let an attacker
who guesses better keep the session.

#### Scenario: Replay revokes the whole family

- **WHEN** a consumed refresh credential is presented again after its grace window has passed
- **THEN** the exchange is refused
- **AND** every credential in that family, including the most recently issued one, becomes
  unusable

#### Scenario: Losing a race is judged by the same rule

- **WHEN** an exchange fails to consume a credential because another exchange consumed it
  first
- **AND** that consumption is outside the grace window
- **THEN** the family is revoked and the exchange is refused, exactly as any other replay

#### Scenario: The legitimate client is also stopped after a replay

- **WHEN** a family has been revoked by a replay
- **AND** the client holding the newest credential attempts an exchange
- **THEN** the exchange is refused
- **AND** that client must authenticate again, unless a client of the same session stored a
  different access token while the exchange was in flight — in which case it continues on
  that token, and is stopped when the token lapses and its own renewal is refused with
  nothing behind it

#### Scenario: Revocation is confined to the affected family

- **WHEN** a family is revoked
- **THEN** credentials belonging to the same user from a separate authentication remain usable

#### Scenario: A credential captured and replayed later is still caught

- **WHEN** a credential is consumed, and is presented again long enough afterwards that no
  legitimate client would still be holding it
- **THEN** the family is revoked
- **AND** the grace window does not exempt it

#### Scenario: A revoked or lapsed session is not resurrected by the grace window

- **WHEN** a consumed credential is presented within its grace window
- **AND** its family has already been revoked, or the session's own lifetime has elapsed
- **THEN** the exchange is refused

### Requirement: Signing out revokes the refresh credential

The system SHALL provide a sign-out operation that revokes the presented credential's family
and clears the credential cookie, so that signing out ends the ability to obtain new access
tokens rather than only discarding client state.

#### Scenario: Sign-out revokes the family

- **WHEN** a signed-in user signs out
- **THEN** the credential cookie is cleared
- **AND** a later exchange using that credential is refused

#### Scenario: Sign-out without a credential is not an error

- **WHEN** a sign-out is requested with no refresh credential present
- **THEN** the request succeeds without error

### Requirement: Expired credentials do not accumulate

The system SHALL remove stored refresh credentials after they are no longer usable, so that
storage does not grow without bound as sessions come and go. A credential ceases to be usable
when it is consumed, not only when its lifetime elapses, and SHALL be reaped on that basis:
retaining a consumed credential for the whole lifetime of the session it belonged to would
make storage grow with the length of the session rather than with the number of sessions.
A consumed credential SHALL still be retained long enough for a replay of it to be detected.

#### Scenario: Expired credentials are reaped

- **WHEN** a stored refresh credential has been expired for longer than the retention period
- **THEN** it is removed from storage without operator intervention

#### Scenario: Consumed credentials are reaped without waiting out the session

- **WHEN** a refresh credential is consumed by a successful exchange
- **THEN** it is removed from storage once the replay-detection window has passed
- **AND** it is not retained until the moment its original lifetime would have elapsed

#### Scenario: A replay arriving within the window is still caught

- **WHEN** a consumed credential is presented again within the replay-detection window
- **THEN** the record is still present
- **AND** the family is revoked

### Requirement: Sessions renew without interrupting the user

The client SHALL obtain a new access token before the current one lapses, and SHALL retry a
single time after a request is refused for an expired access token. A renewal in progress
SHALL be shared by all concurrent callers rather than triggering a renewal each.

#### Scenario: Renewal happens ahead of expiry

- **WHEN** the access token is close enough to expiry to be considered stale
- **THEN** the client obtains a new access token
- **AND** the user is not returned to the sign-in form

#### Scenario: A live conversation survives expiry

- **WHEN** the access token lapses while the user has a conversation open
- **THEN** the session continues after renewal
- **AND** the user is not signed out and does not lose the conversation

#### Scenario: Concurrent callers share one renewal

- **WHEN** several requests need a fresh access token at the same time
- **THEN** exactly one renewal is performed and all callers use its result

#### Scenario: The streaming connection waits for renewal

- **WHEN** the chat connection needs a token while a renewal is in progress
- **THEN** the connection waits for the renewal and then connects
- **AND** it does not fail as though the session had ended

#### Scenario: A failed renewal ends the session honestly

- **WHEN** renewal is refused because the refresh credential is expired or revoked
- **THEN** the user is returned to the sign-in form
- **AND** the reason is reported as an ended session, not as an unreachable server

#### Scenario: Renewal is not attempted without a credential

- **WHEN** no session has been established
- **THEN** the client does not attempt to renew

#### Scenario: A refused request is retried once against a renewed token

- **WHEN** an authenticated request is refused because its access token is not accepted
- **THEN** the client renews and repeats the request once
- **AND** a second refusal is reported to the caller rather than retried again

### Requirement: A returning user with a lapsed access token stays signed in

The refresh credential cannot be read by client script, so the client cannot see whether one
exists. It SHALL therefore record that a session was established, and on returning with an
access token that has lapsed it SHALL attempt renewal before concluding that the user is
signed out. Ending the session SHALL clear that record.

#### Scenario: Returning after the access token has lapsed

- **WHEN** a user returns and the stored access token has expired
- **AND** a session was previously established
- **THEN** the client attempts to renew before deciding what to show
- **AND** the user continues without being asked to sign in again

#### Scenario: Returning when renewal is refused

- **WHEN** a user returns, the access token has lapsed, and renewal is refused
- **THEN** the sign-in form is shown

#### Scenario: A visitor who never signed in is not renewed

- **WHEN** no session was ever established
- **THEN** no renewal is attempted and the sign-in form is shown

#### Scenario: Signing out prevents a later restoration

- **WHEN** a user signs out and later returns
- **THEN** no renewal is attempted and the sign-in form is shown

### Requirement: The user chooses how long the session may be continued

The system SHALL let a user, at the point of authenticating, choose to stay signed
in beyond the ordinary session length. The choice SHALL default to off, so a user
who does not express a preference gets the shorter session. The choice SHALL apply
to registration, password login, and external provider login alike.

#### Scenario: The choice is offered and defaults to off

- **WHEN** a visitor is shown the sign-in form or the registration form
- **THEN** a "Keep me signed in" choice is offered
- **AND** it is not selected until the user selects it

#### Scenario: Opting in yields a long session

- **WHEN** a user authenticates having chosen to stay signed in
- **THEN** the refresh credential remains exchangeable for the extended period
  rather than the ordinary one

#### Scenario: Not opting in yields the ordinary session

- **WHEN** a user authenticates without choosing to stay signed in
- **THEN** the refresh credential remains exchangeable for the ordinary period

#### Scenario: Registration honours the choice

- **WHEN** a visitor registers having chosen to stay signed in
- **THEN** the session issued by that registration is an extended one

#### Scenario: The choice is per authentication, not per user

- **WHEN** a user authenticates on one client choosing to stay signed in
- **AND** the same user authenticates on another client without choosing it
- **THEN** the first session is extended and the second is ordinary
- **AND** neither authentication alters the other's length
### Requirement: An unremembered session ends when the browser closes

Choosing not to stay signed in SHALL be honoured on the machine as well as on the
server: the refresh credential SHALL be held only for as long as the browsing
session lasts, so that closing the browser ends the ability to continue the
session. A remembered credential SHALL be retained across browser restarts until
its lifetime elapses.

#### Scenario: Closing the browser ends an unremembered session

- **WHEN** a user authenticates without choosing to stay signed in
- **AND** the browsing session ends
- **THEN** the refresh credential is no longer presented on later requests
- **AND** the user is asked to sign in again

#### Scenario: A remembered session survives a browser restart

- **WHEN** a user authenticates having chosen to stay signed in
- **AND** the browser is closed and reopened within the extended period
- **THEN** the refresh credential is still presented
- **AND** the session continues without signing in again

#### Scenario: A second tab of an unremembered session is still signed in

- **WHEN** a user authenticates without choosing to stay signed in
- **AND** opens the application again in another tab of the same browser
- **THEN** that tab is signed in
- **AND** the user is not asked to sign in again

Declining to be remembered ends the session when the browser closes; it does not confine
the session to the one tab it was started in.
### Requirement: The choice survives the external provider redirect

Signing in through an external provider leaves and re-enters the application, so
the system SHALL carry the user's choice across that round trip and apply it when
the provider's callback issues the session. The carried choice SHALL be conveyed
so that a third party cannot substitute a longer session than the user asked for.

#### Scenario: Choosing to stay signed in before an external sign-in

- **WHEN** a user chooses to stay signed in and then signs in with Google
- **AND** the provider callback succeeds
- **THEN** the session issued is an extended one

#### Scenario: External sign-in without the choice

- **WHEN** a user signs in with Google without choosing to stay signed in
- **THEN** the session issued is an ordinary one

#### Scenario: A tampered or absent choice falls back to the shorter session

- **WHEN** a provider callback arrives carrying no recognisable choice, or one
  that cannot be trusted as the user's own
- **THEN** the session issued is an ordinary one
### Requirement: Sessions are not silently lengthened

The extended lifetime SHALL apply only to sessions whose user asked for it. A
session already established SHALL NOT become extended without a fresh
authentication, and an extended session SHALL NOT be shortened by later
authentications that did not ask for it.

#### Scenario: An existing ordinary session is not upgraded by exchange

- **WHEN** an ordinary session's credential is exchanged
- **THEN** the successor remains exchangeable only for the ordinary period

#### Scenario: Sessions stored before the choice existed are ordinary

- **WHEN** a refresh credential issued before this capability existed is presented
- **THEN** it is treated as an ordinary session, not an extended one

### Requirement: Concurrent renewal by the legitimate holder does not end the session

Every client of one session presents the same refresh credential, drawn from a store they
share, so two exchanges that overlap in flight necessarily present the same credential twice.
The system SHALL NOT read that as a compromise, and a session in ordinary use across more
than one client SHALL survive its own concurrent renewals.

Consumption SHALL be well defined when exchanges overlap: exactly one of two exchanges of the
same credential SHALL consume it, and the other SHALL be resolved on its merits rather than
by whichever record happens to be written last.

An exchange that did not consume the credential SHALL succeed when the credential was
consumed within a short grace window, issuing a fresh credential in the same family. The
grace window SHALL be short enough that it does not meaningfully widen the period in which a
captured credential is usable, and SHALL be configurable so a deployment can trade tolerance
against exposure.

#### Scenario: Two overlapping exchanges both succeed

- **WHEN** two exchanges of the same refresh credential overlap in flight
- **THEN** both succeed and each receives a usable access token
- **AND** the session's family is not revoked
- **AND** neither client is returned to the sign-in form

#### Scenario: Only one exchange consumes the credential

- **WHEN** two exchanges of the same refresh credential overlap in flight
- **THEN** exactly one of them consumes it
- **AND** the record of its consumption reflects that one exchange, not the later of the two

#### Scenario: Resuming a machine with several clients open

- **WHEN** a machine resumes and several open clients of one session find the access token
  stale and renew together
- **THEN** every exchange succeeds
- **AND** the user is not signed out

#### Scenario: The grace exchange yields a usable credential

- **WHEN** an exchange succeeds under the grace window
- **THEN** the credential it issues belongs to the same session family
- **AND** that credential is itself exchangeable
- **AND** it carries the session's chosen length

#### Scenario: The session continues afterwards

- **WHEN** two clients have renewed concurrently and each holds a credential
- **AND** the session is later renewed again
- **THEN** the renewal succeeds
- **AND** the session is not ended by the credential the other client is holding

#### Scenario: A grace exchange is recorded

- **WHEN** a consumed credential is honoured under the grace window
- **THEN** the occurrence is recorded with the family it belongs to, so that repeated
  occurrences on one family remain visible to an operator

#### Scenario: The grace window is configured and validated

- **WHEN** the system starts with a grace window that is not a positive duration, or that is
  not shorter than the ordinary session length
- **THEN** startup fails with a message naming the setting
- **AND** the system does not serve requests under an unusable value

### Requirement: A credential issued under grace cannot outlive its session

A credential issued because an exchange arrived within the grace window SHALL NOT remain
exchangeable beyond the point at which the credential it was issued against would itself have
ceased to be exchangeable. Otherwise a credential presented within the window — including one
that was captured — would yield a session of full length, unbounded by the session it came
from and unaffected by anything the legitimate user subsequently does.

Once a session has renewed under the grace window, that bound SHALL be inherited by every
credential the session subsequently issues, including those issued by ordinary exchanges.
Without inheritance the bound would survive a single exchange: a credential issued under grace
is otherwise unremarkable, so one ordinary renewal would restore a full lifetime and a
replayed credential would escape the bound at the cost of one further exchange.

A session that has never renewed under the grace window SHALL NOT be bounded, and its
credentials SHALL continue to take their full lifetime measured from the moment each is
issued, so that continued use keeps a session alive indefinitely.

#### Scenario: A grace-issued credential expires with its session

- **WHEN** a credential is issued under the grace window
- **THEN** it ceases to be exchangeable no later than the credential it was issued against
  would have

#### Scenario: An ordinary exchange of an unbounded session is not shortened

- **WHEN** a credential is issued by an ordinary exchange in a session that has never renewed
  under the grace window
- **THEN** it is exchangeable for the session's full chosen length, measured from issue

#### Scenario: An ordinary exchange after a grace exchange stays bounded

- **WHEN** a credential issued under the grace window is later exchanged ordinarily
- **THEN** the credential that exchange issues is bounded by the same point
- **AND** the session is not restored to its full length

#### Scenario: Repeated renewal does not extend a bounded session

- **WHEN** a session that has renewed under the grace window is renewed repeatedly thereafter,
  by either kind of exchange
- **THEN** every credential issued remains bounded by the same point
- **AND** the session cannot be extended by renewing alone

### Requirement: Clients of one session reuse a renewal a sibling has obtained

Clients of one session draw their access token from a store they share, so a token one client
obtains is available to all of them. A client that finds its own token stale SHALL establish
whether a usable token has since been stored before exchanging the refresh credential, and
SHALL use such a token rather than renewing.

A token counts as usable only if it is present, differs from the one the client has already
found wanting, and is not itself close enough to expiry to need renewing. Identity, not
expiry, decides whether it differs: a token can be refused while its expiry still looks good,
so a client that judged by expiry alone would present a repudiated token a second time.

This bounds the overlap the system has to tolerate: a client renews when the session genuinely
needs it, not because it did not look.

#### Scenario: A stale token is not renewed when a sibling has already renewed

- **WHEN** a client finds its access token stale
- **AND** another client of the same session has since stored a usable token
- **THEN** the client uses the stored token
- **AND** no exchange of the refresh credential is made

#### Scenario: A client renews when no sibling has

- **WHEN** a client finds its access token stale
- **AND** no usable token has been stored since
- **THEN** the client exchanges the refresh credential

#### Scenario: A sibling's token that is itself stale is not adopted

- **WHEN** a client finds its access token stale
- **AND** the stored token is also stale
- **THEN** the client exchanges the refresh credential rather than using it

#### Scenario: A token the server has refused is not presented again

- **WHEN** a request is refused for a token that is stored and does not appear expired
- **AND** the client renews in order to retry
- **THEN** the client does not retry with that same token
- **AND** the client exchanges the refresh credential

### Requirement: A refusal does not end a session another client has renewed

A refused exchange means the credential presented was not exchangeable — which, when clients
of one session renew concurrently, may mean only that it was superseded while the exchange was
in flight. The client SHALL therefore establish whether a different token was stored while its
own exchange was outstanding, and SHALL NOT discard the session when one was.

Evidence that the session continues is weaker than a token the client can use, and the two
SHALL be judged separately. Any stored token that is present and differs from the one the
client set out with is evidence a sibling exchanged successfully, whether or not it is close
enough to expiry to be worth adopting. Absence is not evidence: a token that has been removed
means a client of this session ended it deliberately, and the session SHALL then end here too.

Where there is evidence but no usable token, the client SHALL report a failure that leaves the
session intact rather than one that ends it, because the signal for an ended session revokes
the credential family and would end the session for every other client.

Only a refusal with no evidence behind it SHALL end the session for the user. A session that
has genuinely ended therefore still ends: no further token is stored, so the next renewal is
refused with nothing behind it.

#### Scenario: A superseded refusal leaves the session intact

- **WHEN** a client's exchange is refused
- **AND** another client of the same session stored a different, usable token while it was in
  flight
- **THEN** the session is not discarded
- **AND** the client continues on that token
- **AND** no client of the session is returned to the sign-in form

#### Scenario: Evidence without a usable token still spares the session

- **WHEN** a client's exchange is refused
- **AND** the token stored while it was in flight differs but is itself close to expiry
- **THEN** the session is not discarded
- **AND** the client reports a transient failure rather than an ended session
- **AND** the credential family is not revoked

#### Scenario: A genuine refusal ends the session

- **WHEN** a client's exchange is refused
- **AND** no different token was stored while it was in flight
- **THEN** the session is discarded
- **AND** the user is returned to the sign-in form

#### Scenario: A sign-out elsewhere is not mistaken for a sibling's renewal

- **WHEN** a client's exchange is refused
- **AND** the stored token was removed while it was in flight, because another client signed
  out
- **THEN** the session is discarded rather than continued
- **AND** the user is returned to the sign-in form

#### Scenario: A revoked session ends once its tokens lapse

- **WHEN** a session has been revoked
- **AND** a client continues on a token another client stored before the revocation
- **THEN** that token is not renewed once it lapses
- **AND** the session is discarded at that point

#### Scenario: One tab's refusal does not sign out the others

- **WHEN** two clients of one session renew concurrently and one exchange is refused
- **THEN** the client whose exchange succeeded keeps its session
- **AND** the refused client is not signed out on account of the other's success
