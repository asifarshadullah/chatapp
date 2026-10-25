## MODIFIED Requirements

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
