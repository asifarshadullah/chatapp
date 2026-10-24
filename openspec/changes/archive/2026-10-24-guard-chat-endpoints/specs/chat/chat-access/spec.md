## Purpose

Decides which signed-in users may use chat at all, by role permission and by plan, so that
no route to the AI model or to stored conversations skips those checks.

## ADDED Requirements

### Requirement: Messages are sent only over the real-time connection

The system SHALL accept chat messages only over the real-time connection, which requires
the `conversation:create` permission and a plan that includes chat. The system SHALL NOT
offer any HTTP endpoint that accepts a chat message or returns an AI reply.

#### Scenario: Caller without the create permission opens the real-time connection

- **WHEN** a user whose role does not grant `conversation:create` opens the real-time
  connection
- **THEN** the connection is refused with forbidden
- **AND** no message can be sent over it

#### Scenario: Caller whose plan does not include chat sends over the real-time connection

- **WHEN** a user whose role grants `conversation:create` but whose plan does not include
  chat sends a message over the real-time connection
- **THEN** the call fails with the error "Chat is not available on your current plan."
- **AND** no AI reply is generated

#### Scenario: Sending a message over HTTP

- **WHEN** a signed-in user sends a chat message as an HTTP POST to the chat API
- **THEN** the system responds with not found
- **AND** no AI reply is generated
- **AND** no conversation is created

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
