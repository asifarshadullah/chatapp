## Purpose

Keeps a signed-in session usable beyond the access token's lifetime by exchanging a
long-lived, revocable refresh credential for fresh access tokens, while ensuring a stolen
refresh credential is detected on replay and ends the session rather than granting an
attacker an indefinite one.

## ADDED Requirements

### Requirement: Refresh credential issued on authentication

The system SHALL issue a refresh credential whenever it issues an access token through
registration, password login, or external provider callback. The refresh credential SHALL be
delivered only as a cookie that client scripts cannot read, and SHALL never appear in a
response body or URL.

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
issue a distinct successor credential in its place. A consumed credential SHALL NOT be
exchangeable again.

#### Scenario: Successful exchange rotates the credential

- **WHEN** a valid refresh credential is exchanged
- **THEN** a successor credential is issued that differs from the one presented
- **AND** the successor is delivered by the same cookie mechanism

#### Scenario: The successor is usable

- **WHEN** a successor credential from a previous exchange is presented
- **THEN** the exchange succeeds and issues a further successor

#### Scenario: A consumed credential cannot be reused

- **WHEN** a credential that has already been exchanged is presented again
- **THEN** the exchange is refused

### Requirement: Replay of a consumed credential revokes the session family

Successive credentials issued from one authentication SHALL form a family. Because a
legitimate client discards each credential as it is consumed, presenting a consumed
credential indicates the credential was captured and replayed. The system SHALL treat this as
a compromise and revoke every credential in that family, requiring the user to authenticate
again.

#### Scenario: Replay revokes the whole family

- **WHEN** a consumed refresh credential is presented again
- **THEN** the exchange is refused
- **AND** every credential in that family, including the most recently issued one, becomes
  unusable

#### Scenario: The legitimate client is also stopped after a replay

- **WHEN** a family has been revoked by a replay
- **AND** the client holding the newest credential attempts an exchange
- **THEN** the exchange is refused and that client must authenticate again

#### Scenario: Revocation is confined to the affected family

- **WHEN** a family is revoked
- **THEN** credentials belonging to the same user from a separate authentication remain
  usable

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
storage does not grow without bound as sessions come and go.

#### Scenario: Expired credentials are reaped

- **WHEN** a stored refresh credential has been expired for longer than the retention period
- **THEN** it is removed from storage without operator intervention

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
