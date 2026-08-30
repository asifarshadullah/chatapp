## ADDED Requirements

### Requirement: A session that ends by itself is not signed out

Signing out revokes the credential family, which ends the session for every client holding it
and cannot be undone. That is what a user asks for when they sign out, and it is not what has
happened when a client merely finds it can no longer obtain an access token.

A client that discovers its session has ended SHALL discard what it holds locally — the access
token, its expiry, the record that a session was established, and any companion marker — and
SHALL NOT invoke sign-out. It SHALL make no request whose effect is to revoke the credential.

This matters most where the discovery is local. A client can conclude its session is over
without ever asking the server, and in that case the refresh credential may still be perfectly
exchangeable; revoking it would destroy a session that could have continued.

The credential left behind SHALL be allowed to lapse on its own. It is inaccessible to script
and held only by the client that just failed to use it, so its survival for the remainder of
its lifetime is not a compromise, and the system already removes credentials once they are no
longer usable.

Feature: Ending a session without ending the credential
Rule: Revocation follows intent, not circumstance — only a user's sign-out revokes.

#### Scenario: A locally discovered ending does not revoke

- **GIVEN** a signed-in client whose refresh credential is still exchangeable
- **AND** the client can no longer determine that a session was established
- **WHEN** the client concludes the session has ended
- **THEN** no sign-out request is made
- **AND** the credential remains exchangeable

#### Scenario: A refused renewal does not revoke either

- **GIVEN** a signed-in client whose renewal has been refused with no evidence the session
  continues
- **WHEN** the client concludes the session has ended
- **THEN** the client discards the access token, its expiry, and the record of the session
- **AND** no sign-out request is made

#### Scenario: The user is returned to sign-in all the same

- **GIVEN** a client that has concluded its session has ended
- **WHEN** the client discards what it holds
- **THEN** the user is shown the sign-in form
- **AND** the client holds no access token

#### Scenario: A deliberate sign-out still revokes

- **GIVEN** a signed-in user
- **WHEN** the user chooses to sign out
- **THEN** the sign-out request is made
- **AND** a later exchange of that credential is refused

#### Scenario: A credential outliving its client is left to lapse

- **GIVEN** a client that has concluded its session has ended without revoking
- **WHEN** the credential's own lifetime elapses
- **THEN** it ceases to be exchangeable
- **AND** it is removed by the system in the ordinary course
