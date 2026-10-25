## Purpose

Keeps one user from using up a model allowance that every user shares, by limiting how many
messages each user can send in a day.

## ADDED Requirements

### Requirement: Each user may send a limited number of messages per day

The system SHALL accept at most the configured number of messages, 50 by default, from each
user per UTC day, whichever model answers. A message SHALL count once it is accepted: after its
content, the model it names, the caller's plan and the conversation it names have been
checked. A message refused for any of those
reasons SHALL NOT count. A message that is accepted and then fails at the model SHALL count.
The limit SHALL hold even when one user sends several messages at once.

#### Scenario: Messages under the limit are accepted

- **WHEN** a user who has sent fewer messages today than the limit sends another
- **THEN** the message is accepted and answered

#### Scenario: A message over the limit is refused

- **WHEN** a user who has already sent the limit's number of messages today sends another
- **THEN** the system refuses it before the stream starts with 429 Too Many Requests and an
  error message stating the daily limit and that it resets at midnight UTC
- **AND** the message is not saved and the model is not asked

#### Scenario: The limit belongs to each user

- **WHEN** one user has reached the limit and another user sends a message
- **THEN** the other user's message is accepted

#### Scenario: The limit resets each UTC day

- **WHEN** a user who reached the limit sends a message after the next midnight UTC
- **THEN** the message is accepted

#### Scenario: A refused message does not count

- **WHEN** a user's message is refused for its content, the model it names, the user's plan or
  the conversation it names
- **THEN** it does not count towards the user's limit

#### Scenario: A message that fails at the model counts

- **WHEN** a user's accepted message fails at the model
- **THEN** it counts towards the user's limit

#### Scenario: Messages sent at once cannot pass the limit

- **WHEN** a user one message below the limit sends several messages at the same moment
- **THEN** exactly one of them is accepted

### Requirement: The limit is configurable and must be positive

The system SHALL read the daily limit from configuration and SHALL refuse to start when it is
not a positive whole number.

#### Scenario: A configured limit applies

- **WHEN** the daily limit is configured as 3
- **THEN** the fourth message a user sends in a UTC day is refused

#### Scenario: A limit that is not positive is refused

- **WHEN** the app starts with a daily limit of zero or less
- **THEN** it does not start
