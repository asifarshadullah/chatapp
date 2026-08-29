## ADDED Requirements

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

## MODIFIED Requirements

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
