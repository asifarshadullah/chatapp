## REMOVED Requirements

### Requirement: A conversation belongs to the user who starts it

**Reason**: Its scenarios name the real-time connection, which is removed, and one of them
covers how that connection was carried, which no longer applies.
**Migration**: Replaced by "A conversation is owned by the user who starts it", which
describes the same rule for the streaming endpoint.

### Requirement: Only the owner can add to a conversation

**Reason**: Its refusal scenario names the real-time connection and its error, which are
removed.
**Migration**: Replaced by "Only the owner can send into a conversation", which gives the
same rule and the streaming endpoint's response.

### Requirement: A message is validated before its conversation is looked up

**Reason**: Its scenario names the real-time connection and its error, which are removed.
**Migration**: Replaced by "A message's content is checked before its conversation", which
gives the same rule and the streaming endpoint's response.

### Requirement: Refusing a conversation does not reveal that it exists

**Reason**: Its sending scenario names the real-time connection and its error, which are
removed.
**Migration**: Replaced by "A refused conversation looks the same as a missing one", which
gives the same rule for history and the streaming endpoint.

## ADDED Requirements

### Requirement: A conversation is owned by the user who starts it

The system SHALL record the signed-in user who starts a conversation as its owner.
Ownership SHALL NOT change for the life of the conversation.

#### Scenario: Sending without a conversation id starts a conversation owned by the sender

- **WHEN** a signed-in user sends a message without a conversation id to the streaming
  endpoint
- **THEN** a new conversation is created with that user as its owner
- **AND** the stream carries the new conversation's id

### Requirement: Only the owner can send into a conversation

The system SHALL accept a message into an existing conversation only from its owner. A
refused message SHALL leave the conversation unchanged, SHALL NOT be sent to the AI model,
and SHALL NOT start a new conversation in its place.

#### Scenario: Owner continues their conversation

- **WHEN** a user sends a message to the streaming endpoint naming a conversation they own
- **THEN** the stream carries that conversation's id
- **AND** the message and the AI reply are added to that conversation

#### Scenario: Another user sends into the conversation

- **WHEN** a signed-in user sends a message to the streaming endpoint naming a conversation
  owned by someone else
- **THEN** the system responds with not found and the error "Conversation not found."
- **AND** no conversation id and no reply tokens are sent to the caller
- **AND** the owner's conversation is unchanged
- **AND** no new conversation is created

### Requirement: A message's content is checked before its conversation

The system SHALL validate the message itself before looking at the conversation it names.
A message that is empty, whitespace only, or longer than the allowed length SHALL be refused
for its content, whatever conversation it names, so the answer reveals nothing about that
conversation.

#### Scenario: Empty message naming another user's conversation

- **WHEN** a signed-in user sends an empty message to the streaming endpoint naming a
  conversation owned by someone else
- **THEN** the system responds with bad request and the error "Message cannot be empty."
- **AND** the owner's conversation is unchanged

#### Scenario: Over-long message naming another user's conversation

- **WHEN** a signed-in user sends a message longer than the allowed length to the streaming
  endpoint naming a conversation owned by someone else
- **THEN** the system responds with bad request and an error that the message is too long
- **AND** the owner's conversation is unchanged

### Requirement: A refused conversation looks the same as a missing one

The system SHALL answer a request for a conversation the caller does not own exactly as it
answers a request for a conversation id that does not exist, on every path that accepts a
conversation id.

#### Scenario: Unknown id on history

- **WHEN** a user whose role grants `conversation:read` requests the history of a
  conversation id that does not exist
- **THEN** the system responds with not found

#### Scenario: Unknown id when sending

- **WHEN** a signed-in user sends a message to the streaming endpoint naming a conversation
  id that does not exist
- **THEN** the system responds with not found and the error "Conversation not found.", the
  same response as for a conversation owned by someone else
