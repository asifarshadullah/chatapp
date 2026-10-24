## REMOVED Requirements

### Requirement: Messages are sent only over the real-time connection

**Reason**: The real-time connection is removed. Messages are sent as an HTTP request whose
response streams the reply, which makes the same checks.
**Migration**: Send `POST /api/chat/messages` with the message and an optional conversation
id, and read the reply from the event stream it returns. See "Messages are sent only through
the streaming endpoint".

## ADDED Requirements

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
