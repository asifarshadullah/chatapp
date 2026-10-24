# Conversation Ownership Specification

## Purpose

Keeps each conversation private to the user who started it, so that holding a
conversation's id is never enough to read it, add to it, or learn that it exists.

## Requirements

### Requirement: A conversation belongs to the user who starts it

The system SHALL record the signed-in user who starts a conversation as its owner.
Ownership SHALL NOT change for the life of the conversation.

#### Scenario: Sending without a conversation id starts a conversation owned by the sender

- **WHEN** a signed-in user sends a message without a conversation id over the real-time
  connection
- **THEN** a new conversation is created with that user as its owner
- **AND** the response carries the new conversation's id

#### Scenario: Ownership does not depend on how the real-time connection is carried

- **WHEN** a signed-in user sends a message without a conversation id over a real-time
  connection, whether it runs over a persistent socket or falls back to repeated short
  HTTP requests
- **THEN** a new conversation is created with that user as its owner

### Requirement: Only the owner can read a conversation

The system SHALL return a conversation's history only to its owner. No role or permission
SHALL grant access to another user's conversation. These scenarios concern callers whose
role grants `conversation:read`; a caller without it is refused before ownership is
considered (see chat-access).

#### Scenario: Owner reads their own history

- **WHEN** a user whose role grants `conversation:read` requests the history of a
  conversation they own
- **THEN** the system returns every message in it, in chronological order

#### Scenario: Another user requests the history

- **WHEN** a user whose role grants `conversation:read` requests the history of a
  conversation owned by someone else
- **THEN** the system responds with not found
- **AND** no message from that conversation is returned

#### Scenario: An administrator requests another user's history

- **WHEN** a user whose role grants every permission requests the history of a
  conversation owned by someone else
- **THEN** the system responds with not found

### Requirement: Only the owner can add to a conversation

The system SHALL accept a message into an existing conversation only from its owner. A
refused message SHALL leave the conversation unchanged, SHALL NOT be sent to the AI model,
and SHALL NOT start a new conversation in its place.

#### Scenario: Owner continues their conversation

- **WHEN** a user sends a message naming a conversation they own
- **THEN** the message and the AI reply are added to that conversation

#### Scenario: Another user sends into the conversation over the real-time connection

- **WHEN** a signed-in user sends a message over the real-time connection naming a
  conversation owned by someone else
- **THEN** the call fails with the error "Conversation not found."
- **AND** no conversation id and no reply tokens are sent to the caller
- **AND** the owner's conversation is unchanged
- **AND** no new conversation is created

### Requirement: A message is validated before its conversation is looked up

The system SHALL validate the message itself before looking at the conversation it names.
A message that is empty, whitespace only, or longer than the allowed length SHALL be refused
for its content, whatever conversation it names, so the answer reveals nothing about that
conversation.

#### Scenario: Empty message naming another user's conversation over the real-time connection

- **WHEN** a signed-in user sends an empty message over the real-time connection naming a
  conversation owned by someone else
- **THEN** the call fails with the error "Message cannot be empty."
- **AND** the owner's conversation is unchanged

### Requirement: Refusing a conversation does not reveal that it exists

The system SHALL answer a request for a conversation the caller does not own exactly as it
answers a request for a conversation id that does not exist, on every path that accepts a
conversation id.

#### Scenario: Unknown id on history

- **WHEN** a user whose role grants `conversation:read` requests the history of a
  conversation id that does not exist
- **THEN** the system responds with not found

#### Scenario: Unknown id when sending over the real-time connection

- **WHEN** a signed-in user sends a message over the real-time connection naming a
  conversation id that does not exist
- **THEN** the call fails with the error "Conversation not found.", the same error as for
  a conversation owned by someone else

### Requirement: Conversations without an owner are unreachable

The system SHALL treat a stored conversation that has no recorded owner as not existing,
for every user.

#### Scenario: A conversation stored before ownership existed

- **WHEN** any signed-in user requests the history of, or sends a message into, a stored
  conversation that has no recorded owner
- **THEN** the system responds as it does for a conversation id that does not exist
