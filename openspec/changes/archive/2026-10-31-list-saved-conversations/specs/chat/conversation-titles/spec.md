## Purpose

Gives every conversation a short, recognisable title: its first message at once, replaced by
a title the model writes after the first reply, so the conversation list has something to
show.

## ADDED Requirements

### Requirement: A new conversation is titled with its first message

When a message starts a conversation, the system SHALL store a title made from that message:
leading and trailing whitespace removed and every run of whitespace, line breaks included,
replaced by one space. Lengths SHALL be counted in Unicode code points, so a cut never
splits a character such as an emoji. A result longer than 60 characters SHALL be cut at the
last space within its first 59 characters, or at 59 characters when there is no such space,
and end with "…". A result of 60 characters or fewer SHALL be used whole.

#### Scenario: A short first message

- **WHEN** a user starts a conversation with "  What is" followed by a line break and
  "  a monad? "
- **THEN** the conversation's title is "What is a monad?"

#### Scenario: A long first message

- **WHEN** a user starts a conversation with a message longer than 60 characters that has
  spaces in its first 59 characters
- **THEN** the title is the message up to its last space within the first 59 characters,
  followed by "…"
- **AND** the title is at most 60 characters long

#### Scenario: A long first message without spaces

- **WHEN** a user starts a conversation with 80 characters and no spaces
- **THEN** the title is the first 59 characters followed by "…"

#### Scenario: A cut that falls on an emoji

- **WHEN** a user starts a conversation with 58 letters followed by emoji and no spaces
- **THEN** the title is the 58 letters and the first emoji whole, followed by "…"

### Requirement: The model replaces the title after the first reply

After the reply to the message that started a conversation has streamed to its end, the
system SHALL ask the model option that answered for a short title for a conversation
beginning with that message, and SHALL store it in place of the first-message title before
it sends the end marker. The title SHALL be stored even if the caller leaves while it is
being generated. The model's text SHALL be cleaned before use:
- only its first line that is not blank is kept;
- a leading "Title:" in any letter case is removed;
- surrounding whitespace and surrounding quotes or backticks (", ', “ ”, ‘ ’ and `) are
  removed;
- a single trailing full stop is removed;
- the result is cut as a first-message title is cut.

Only that first reply SHALL lead to a title request. A conversation whose first reply did not
stream to its end, and a conversation saved before titles existed, SHALL keep its
first-message title.

#### Scenario: The first reply ends

- **WHEN** a user starts a conversation and its reply streams to its end
- **THEN** the system asks the model that answered for a title
- **AND** the conversation's title is the model's cleaned title by the time the end marker
  arrives

#### Scenario: The model decorates the title

- **WHEN** the model answers the title request with a blank line, then
  `Title: “Understanding Monads.”`, then a second line
- **THEN** the title stored is "Understanding Monads"

#### Scenario: A later reply

- **WHEN** a user sends a second message into a conversation and its reply streams to its end
- **THEN** no title is requested and the conversation's title is unchanged

#### Scenario: The caller leaves while the title is generated

- **WHEN** the first reply of a new conversation has streamed to its end and the caller
  closes the request while the model's title is being generated
- **THEN** the model's title is still stored

### Requirement: A title that cannot be generated leaves the first-message title

The system SHALL keep the first-message title when the title request fails for any reason,
including the model service's rate limit, takes longer than 10 seconds, or yields no text
after cleaning. Such a failure SHALL NOT change the reply stream, which still ends with the
end marker and no error, and SHALL NOT be affected by a failure to store the title. The
system SHALL NOT request a title when the first reply fails, ends without any text, or the
caller leaves before it ends.

#### Scenario: The title request fails

- **WHEN** the first reply of a new conversation streams to its end and the title request
  then fails
- **THEN** the stream ends with the end marker and no error event
- **AND** the conversation keeps its first-message title

#### Scenario: The title request is too slow

- **WHEN** the title request has produced nothing 10 seconds after it was made
- **THEN** the stream ends with the end marker
- **AND** the conversation keeps its first-message title

#### Scenario: The first reply has no text

- **WHEN** the model ends the first reply of a new conversation without sending any text
- **THEN** no title is requested and the conversation keeps its first-message title

#### Scenario: The first reply fails part-way

- **WHEN** the first reply of a new conversation fails after its first token
- **THEN** no title is requested and the conversation keeps its first-message title
- **AND** a later reply that streams to its end does not request one either

### Requirement: A title request is not a message

A title request SHALL NOT count toward the user's daily message limit, and neither the
request nor the title SHALL be added to the conversation's messages.

#### Scenario: Starting a conversation counts one message

- **WHEN** a user with two messages left today starts a conversation, its reply streams to
  its end and the conversation is titled
- **THEN** the user can still send one more message today

#### Scenario: The history after a title is generated

- **WHEN** a conversation has been titled by the model
- **THEN** its history holds only the user's message and the reply
