## MODIFIED Requirements

### Requirement: Refusals are answered before the stream starts

The system SHALL decide whether to accept a message before it sends any part of the stream.
A refused message SHALL get an ordinary error response with a status code, and SHALL NOT
receive a conversation id, a token or any other stream event. A refusal for the message's
content, the model it names, the caller's plan, the caller's daily message limit or the
conversation it names SHALL also carry an error message. A refusal because the caller is not
signed in or lacks the permission SHALL carry the status code only.

#### Scenario: A message refused for its content, plan or conversation

- **WHEN** a message is refused for its content, the model it names, the caller's plan, or the
  conversation it names
- **THEN** the response carries an error status code and an error message
- **AND** it is not an event stream and carries no conversation id or token

#### Scenario: A message refused for the daily limit

- **WHEN** a message is refused because the caller has reached their daily message limit
- **THEN** the response carries 429 Too Many Requests and an error message
- **AND** it is not an event stream and carries no conversation id or token

#### Scenario: A message refused for sign-in or permission

- **WHEN** a message is refused because the caller is not signed in or their role lacks
  the permission
- **THEN** the response carries the error status code
- **AND** it is not an event stream and carries no conversation id or token

### Requirement: A failure before the first token is an ordinary error

The system SHALL answer a reply that fails before the model produces its first token with an
ordinary error response, not an event stream. When the model service refused because its own
rate limit was reached, the response SHALL carry 503 Service Unavailable and the error message
"The demo has reached its model limit for now. Please try again later."; any other failure
SHALL carry bad gateway and the error message "The reply could not be completed.". Neither
SHALL disclose internal detail, and no reply SHALL be saved.

#### Scenario: The model fails before its first token

- **WHEN** the model fails before producing any token of a reply
- **THEN** the system responds with bad gateway and the error "The reply could not be
  completed."
- **AND** the response reveals no internal detail
- **AND** it is not an event stream and carries no conversation id or token
- **AND** no reply is saved

#### Scenario: The model service is at its rate limit

- **WHEN** the model service refuses a reply because its rate limit has been reached, before
  producing any token
- **THEN** the system responds with 503 Service Unavailable and the error "The demo has
  reached its model limit for now. Please try again later."
- **AND** the response reveals no internal detail
- **AND** it is not an event stream and carries no conversation id or token
- **AND** no reply is saved
