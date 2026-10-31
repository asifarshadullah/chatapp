## ADDED Requirements

### Requirement: Listing conversations requires the read permission

The system SHALL return the conversation list only to a signed-in caller whose role grants
`conversation:read`. A caller without it SHALL be refused with forbidden and no list. Listing
SHALL NOT depend on the caller's plan.

#### Scenario: Caller who is not signed in requests the list

- **WHEN** a caller who is not signed in requests the conversation list
- **THEN** the system responds with unauthorized

#### Scenario: Caller without the read permission requests the list

- **WHEN** a user whose role does not grant `conversation:read` requests the conversation
  list
- **THEN** the system responds with forbidden
- **AND** no conversation is returned

#### Scenario: Caller whose plan does not include chat requests the list

- **WHEN** a user whose role grants `conversation:read` but whose plan does not include
  chat requests the conversation list
- **THEN** the system returns their conversations
