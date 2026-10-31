# Chat Access Specification

## Purpose

Decides which signed-in users may use chat at all, by role permission and by plan, so that
no route to the AI model or to stored conversations skips those checks.

## Requirements

### Requirement: Messages are sent only through the streaming endpoint

The system SHALL accept chat messages only through the streaming endpoint, which requires a
signed-in caller, the `conversation:create` permission and a plan that includes chat. A
refused message SHALL NOT reach the AI model and SHALL NOT create a conversation. The system
SHALL NOT offer a real-time connection for chat.

#### Scenario: Caller who is not signed in sends a message

- **WHEN** a caller who is not signed in sends a message to the streaming endpoint
- **THEN** the system responds with unauthorized
- **AND** no AI reply is generated
- **AND** no conversation is created

#### Scenario: Caller without the create permission sends a message

- **WHEN** a user whose role does not grant `conversation:create` sends a message to the
  streaming endpoint
- **THEN** the system responds with forbidden
- **AND** no AI reply is generated
- **AND** no conversation is created

#### Scenario: Caller with the create permission sends a message

- **WHEN** a user whose role grants `conversation:create`, and whose plan includes chat,
  sends a message to the streaming endpoint
- **THEN** the system streams the AI reply

#### Scenario: Caller whose plan does not include chat sends a message

- **WHEN** a user whose role grants `conversation:create` but whose plan does not include
  chat sends a message to the streaming endpoint
- **THEN** the system responds with forbidden and the error "Chat is not available on your
  current plan."
- **AND** no AI reply is generated
- **AND** no conversation is created

#### Scenario: Opening a real-time chat connection

- **WHEN** a signed-in user tries to open the real-time chat connection the system used to
  offer
- **THEN** the system responds with not found

### Requirement: Reading a conversation's history requires the read permission

The system SHALL return a conversation's history only to a caller whose role grants the
`conversation:read` permission. A caller without it SHALL be refused with forbidden before
the conversation is looked up, so the answer is the same whichever conversation id they
name. Reading history SHALL NOT depend on the caller's plan.

#### Scenario: Caller with the read permission reads their own history

- **WHEN** a user whose role grants `conversation:read` requests the history of a
  conversation they own
- **THEN** the system returns every message in it, in chronological order

#### Scenario: Caller without the read permission requests their own history

- **WHEN** a user whose role does not grant `conversation:read` requests the history of a
  conversation they own
- **THEN** the system responds with forbidden
- **AND** no message from that conversation is returned

#### Scenario: Caller without the read permission requests another user's or an unknown conversation

- **WHEN** a user whose role does not grant `conversation:read` requests the history of a
  conversation owned by someone else, or of a conversation id that does not exist
- **THEN** the system responds with forbidden, the same response as for their own
  conversation

#### Scenario: Caller whose plan does not include chat reads their own history

- **WHEN** a user whose role grants `conversation:read` but whose plan does not include
  chat requests the history of a conversation they own
- **THEN** the system returns every message in it, in chronological order

#### Scenario: A role that grants every permission can read

- **WHEN** a user whose role grants every permission requests the history of a
  conversation they own
- **THEN** the system returns every message in it, in chronological order

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
