## ADDED Requirements

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

## MODIFIED Requirements

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
- **THEN** the exchange is refused and that client must authenticate again

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
