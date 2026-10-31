## Purpose

Lets a signed-in user list their own saved conversations, most recently active first and a
page at a time, so a conversation can be found again without its link.

## ADDED Requirements

### Requirement: The caller's conversations are listed by latest activity

The system SHALL offer a conversation list endpoint that returns the caller's conversations
ordered by their last activity, newest first, where a conversation's last activity is the
time of its latest message, given in UTC. Conversations with the same last activity SHALL be
returned in a fixed order. Each entry SHALL carry the conversation's id, its title and its
last activity, and SHALL NOT carry its messages.

#### Scenario: Listing two conversations

- **WHEN** a user who has two conversations, A whose latest message is older than B's,
  requests the list
- **THEN** the system returns B and then A
- **AND** each entry carries the conversation's id, title and last activity time in UTC,
  and no messages

#### Scenario: Continuing a conversation moves it to the top

- **WHEN** a user who has conversations A and B, with B the most recently active, sends a
  message into A
- **AND** the user then requests the list
- **THEN** A comes before B

#### Scenario: A user with no conversations

- **WHEN** a user who has no conversations requests the list
- **THEN** the system returns an empty list and no cursor for a further page

### Requirement: Only conversations with a reply are listed

The system SHALL list a conversation only once at least one reply, whole or part, has been
saved to it. This is exactly the set of conversations whose id the caller was sent, since
the id arrives with the first token. A conversation whose first reply failed or was abandoned
before its first token SHALL NOT be listed, though its message stays saved.

#### Scenario: The first reply fails before its first token

- **WHEN** a user's message starts a conversation and the model fails, or the model service
  is at its rate limit, before producing any token
- **THEN** that conversation does not appear in the user's list

#### Scenario: The first reply fails part-way

- **WHEN** a user's message starts a conversation and the model fails after producing its
  first token
- **THEN** that conversation appears in the user's list

#### Scenario: The caller leaves after the first token

- **WHEN** a user closes the request after receiving part of a new conversation's first
  reply
- **THEN** that conversation appears in the user's list

### Requirement: The list is returned a page at a time

The system SHALL return at most `limit` conversations per request, 20 when the caller gives
no limit. A limit that is not a whole number SHALL be refused with bad request. A limit
below 1 or above 50 SHALL be refused with bad request and the error "Limit must be between 1
and 50.". When more conversations follow the page, the response SHALL carry a cursor;
requesting the list with that cursor SHALL return the conversations that follow, and the
last page SHALL carry no cursor. When nothing changes between requests, following the
cursors from the first page SHALL return every listed conversation exactly once. A cursor
that is not in the form the system issues SHALL be refused with bad request and the error
"Invalid cursor.". A cursor only positions the caller within their own list, so it SHALL
NOT reveal or return another user's conversations whatever it holds.

#### Scenario: Paging through 25 conversations

- **WHEN** a user who has 25 conversations requests the list without a limit
- **THEN** the system returns the 20 most recently active and a cursor
- **AND** requesting the list with that cursor returns the remaining 5 and no cursor

#### Scenario: A smaller page

- **WHEN** a user who has 3 conversations requests the list with a limit of 2
- **THEN** the system returns the 2 most recently active and a cursor

#### Scenario: A limit out of range

- **WHEN** a user requests the list with a limit of 0 or 51
- **THEN** the system responds with bad request and the error "Limit must be between 1 and
  50."

#### Scenario: A limit that is not a number

- **WHEN** a user requests the list with a limit of "abc"
- **THEN** the system responds with bad request

#### Scenario: A malformed cursor

- **WHEN** a user requests the list with a cursor that is not in the form the system
  issues
- **THEN** the system responds with bad request and the error "Invalid cursor."

#### Scenario: Another user's cursor

- **WHEN** user A requests the list with a cursor issued to user B
- **THEN** the response holds only A's conversations

### Requirement: Conversations saved before the list existed are listed

The system SHALL list conversations saved before titles and last activity were recorded, when
they have a reply. Such a conversation SHALL get the title its first message would give it
(see conversation-titles), and its latest message's time as its last activity. Conversations
that have no recorded owner SHALL stay unlisted and unreachable.

#### Scenario: A conversation stored without a title or last activity

- **WHEN** a conversation was stored with a message and a reply but no title and no last
  activity time
- **AND** its owner requests the list
- **THEN** the entry's title is its first message, cut as a first-message title is cut
- **AND** its last activity is the time of its latest message

#### Scenario: A conversation stored without an owner

- **WHEN** a conversation was stored with no recorded owner
- **THEN** it appears in no user's list
