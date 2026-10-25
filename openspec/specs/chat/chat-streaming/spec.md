# Chat Streaming Specification

## Purpose

Defines how an AI reply reaches the caller: as an event stream over one HTTP request that
carries the conversation id, the reply token by token and an end marker, and how refusals,
failures part-way through and a caller who goes away are handled.

## Requirements

### Requirement: A reply streams as conversation id, tokens, then an end marker

The system SHALL answer an accepted message whose reply has at least one token with an event
stream that carries, in order, the conversation's id, each token of the AI reply as the
model produces it, and an end marker. Token text SHALL arrive exactly as the model produced
it, line breaks included. When the stream ends, the tokens joined in order SHALL equal the
reply that is saved to the conversation.

#### Scenario: A message streams its reply

- **WHEN** a signed-in user whose role and plan allow chat sends a message
- **AND** the model replies with at least one token
- **THEN** the response is an event stream
- **AND** its first event carries the conversation's id
- **AND** the following events carry the reply's tokens in the order the model produced
  them
- **AND** its last event is the end marker

#### Scenario: The saved reply matches the streamed tokens

- **WHEN** a reply has streamed to its end marker
- **THEN** the conversation's history holds the user's message followed by a reply whose
  text is the streamed tokens joined in order

#### Scenario: A token containing a line break

- **WHEN** the model produces a token that contains a line break
- **THEN** the caller receives that token with the line break intact, as a single token

### Requirement: Tokens reach the caller as they are produced

The system SHALL send each token to the caller as soon as the model produces it, not hold
the reply back until it is complete.

#### Scenario: The first token arrives before the reply is finished

- **WHEN** the model has produced the first token of a reply and not yet the next one
- **THEN** the caller has already received the conversation id and that first token

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
SHALL carry bad gateway and the error message "The reply could not be completed.". A reply
that ends without producing any token, for example because a reasoning model spent its whole
length limit thinking, SHALL count as a failure of the second kind. Neither SHALL disclose
internal detail, and no reply SHALL be saved.

#### Scenario: The model fails before its first token

- **WHEN** the model fails before producing any token of a reply
- **THEN** the system responds with bad gateway and the error "The reply could not be
  completed."
- **AND** the response reveals no internal detail
- **AND** it is not an event stream and carries no conversation id or token
- **AND** no reply is saved

#### Scenario: The model produces no text

- **WHEN** the model ends a reply without producing any token
- **THEN** the system responds with bad gateway and the error "The reply could not be
  completed."
- **AND** it is not an event stream and carries no conversation id or token

#### Scenario: The model service is at its rate limit

- **WHEN** the model service refuses a reply because its rate limit has been reached, before
  producing any token
- **THEN** the system responds with 503 Service Unavailable and the error "The demo has
  reached its model limit for now. Please try again later."
- **AND** the response reveals no internal detail
- **AND** it is not an event stream and carries no conversation id or token
- **AND** no reply is saved

### Requirement: A failure part-way through ends the stream with an error

The system SHALL end the stream with an error event if the reply fails after the stream has
started. The error message SHALL NOT disclose internal detail. The reply SHALL be saved with
the tokens the model produced before it failed, and the user's message SHALL stay saved.

#### Scenario: The model fails after the first token

- **WHEN** the model fails after producing the first token of a reply
- **THEN** the caller receives the conversation id and that token
- **AND** the stream ends with an error event whose message reveals no internal detail
- **AND** no end marker is sent
- **AND** the conversation holds the user's message and a reply made of the tokens produced
  before the failure

### Requirement: A caller who goes away stops the reply

The system SHALL stop generating a reply when the caller closes the request before the
reply is complete. The reply SHALL be saved with the tokens the model produced before it
stopped, and the user's message SHALL stay saved.

#### Scenario: The caller closes the request part-way through a reply

- **WHEN** the caller closes the request after receiving part of a reply
- **THEN** the model stops generating that reply
- **AND** the conversation holds the user's message and a reply made of the tokens produced
  before it stopped
