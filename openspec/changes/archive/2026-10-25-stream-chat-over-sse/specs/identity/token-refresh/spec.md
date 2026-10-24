## MODIFIED Requirements

### Requirement: Sessions renew without interrupting the user

The client SHALL obtain a new access token before the current one lapses, and SHALL retry a
single time after a request is refused for an expired access token. A renewal in progress
SHALL be shared by all concurrent callers rather than triggering a renewal each. Opening
the chat SHALL check the session, so a session that can no longer be renewed is noticed
when the chat opens rather than on the first message.

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

- **WHEN** a chat message is sent while a renewal is in progress
- **THEN** the message waits for the renewal and is then sent with the renewed token
- **AND** it does not fail as though the session had ended

#### Scenario: A failed renewal ends the session honestly

- **WHEN** renewal is refused because the refresh credential is expired or revoked
- **THEN** the user is returned to the sign-in form
- **AND** the reason is reported as an ended session, not as an unreachable server

#### Scenario: Opening the chat with a token that can no longer be renewed

- **WHEN** the chat opens with an access token that is stale but not yet expired
- **AND** renewal is refused
- **THEN** the user is returned to the sign-in form before sending anything
- **AND** the refresh credential is not revoked

#### Scenario: Renewal is not attempted without a credential

- **WHEN** no session has been established
- **THEN** the client does not attempt to renew

#### Scenario: A refused request is retried once against a renewed token

- **WHEN** an authenticated request is refused because its access token is not accepted
- **THEN** the client renews and repeats the request once
- **AND** a second refusal is reported to the caller rather than retried again
