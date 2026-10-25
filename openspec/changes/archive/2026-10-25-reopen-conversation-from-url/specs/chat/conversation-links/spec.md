## Purpose

Lets a user get back to a conversation after a reload, in a new tab or from a saved link, by
naming the open conversation in the page's URL and reopening it with its saved history.

## ADDED Requirements

### Requirement: The open conversation is named in the URL

The chat SHALL put the open conversation's id in the page's URL as the `conversation` query
parameter as soon as the reply stream names it. Updating the URL SHALL NOT add a browser
history entry and SHALL leave the rest of the URL unchanged.

#### Scenario: Starting a conversation puts its id in the URL

- **WHEN** a user sends the first message of an empty chat and the reply stream names the
  new conversation
- **THEN** the URL carries `conversation=<that id>`
- **AND** going back in the browser does not return to the URL without it

#### Scenario: Continuing a conversation keeps its id in the URL

- **WHEN** a user sends another message in a conversation named in the URL
- **THEN** the URL still names that conversation

### Requirement: A conversation named in the URL reopens with its history

When the chat opens with a URL that names a conversation the user owns, it SHALL show every
saved message of that conversation in order, and a message the user then sends SHALL continue
that conversation. No message SHALL be sent until the history is shown.

#### Scenario: Reloading the page reopens the conversation

- **WHEN** a user reloads the page while the URL names their conversation
- **THEN** the chat shows every saved message of that conversation, in order
- **AND** a message the user then sends is added to that conversation

#### Scenario: Opening the URL in a new tab reopens the conversation

- **WHEN** a signed-in user opens a URL naming their conversation in a new tab
- **THEN** the chat shows every saved message of that conversation, in order

#### Scenario: Reloading during a reply keeps what was produced

- **WHEN** a user reloads the page while a reply to a message in a conversation named in the
  URL is streaming
- **THEN** the reopened conversation shows the user's message
- **AND** once the system has saved the part of the reply produced before the reload, the
  conversation shows it after that message

#### Scenario: Loading the history

- **WHEN** the chat is still loading the history of the conversation named in the URL
- **THEN** it shows that it is loading, not the empty chat
- **AND** the user cannot send a message

#### Scenario: Returning from the billing page reopens the conversation

- **WHEN** a user with an open conversation goes to manage their plan and comes back
- **THEN** the chat shows that conversation's saved messages

### Requirement: A conversation the user cannot open is not found

When the URL names a conversation that does not exist, belongs to someone else, or is not a
valid id, the chat SHALL show "Conversation not found.", remove the id from the URL, and
start an empty chat. No message of that conversation SHALL be shown. A value that is not a
valid conversation id SHALL NOT be sent to the server.

#### Scenario: The URL names another user's conversation

- **WHEN** a signed-in user opens a URL naming a conversation owned by someone else
- **THEN** the chat shows "Conversation not found." and no message of that conversation
- **AND** the URL no longer names a conversation
- **AND** a message the user then sends starts a new conversation

#### Scenario: The URL names an unknown conversation

- **WHEN** a signed-in user opens a URL naming a conversation id that does not exist
- **THEN** the chat shows "Conversation not found."
- **AND** the URL no longer names a conversation

#### Scenario: The URL names something that is not a conversation id

- **WHEN** a signed-in user opens a URL whose `conversation` value is not a conversation id,
  such as a relative path
- **THEN** the chat shows "Conversation not found."
- **AND** no request is made with that value
- **AND** the URL no longer names a conversation

### Requirement: History that fails to load for another reason can be retried

When the history of the conversation named in the URL cannot be loaded for any reason other
than not found or an ended session, the chat SHALL show "Your conversation could not be
loaded." and keep the id in the URL, so reloading tries again. No message SHALL be sent into
that conversation while its history is not shown.

#### Scenario: The server cannot be reached while loading history

- **WHEN** the chat opens with a URL naming the user's conversation and the server cannot be
  reached
- **THEN** the chat shows "Your conversation could not be loaded."
- **AND** the URL still names the conversation
- **AND** the user cannot send a message into it

#### Scenario: The session has ended while loading history

- **WHEN** the chat opens with a URL naming a conversation and the session turns out to
  have ended
- **THEN** the user is sent to sign in
- **AND** the URL still names the conversation

### Requirement: New chat starts an empty conversation

The chat SHALL offer a New chat action whenever it shows messages or an error, and SHALL
make it unavailable while a reply is streaming. It SHALL clear the shown messages and any error and remove the id from the URL,
so the next message starts a new conversation. The previous conversation SHALL stay
reachable through its URL.

#### Scenario: Starting a new chat

- **WHEN** a user with an open conversation chooses New chat
- **THEN** the chat is empty and shows no error and the URL no longer names a conversation
- **AND** a message the user then sends starts a new conversation

#### Scenario: Leaving a conversation that failed to load

- **WHEN** the history of the conversation named in the URL failed to load and the user
  chooses New chat
- **THEN** the chat is empty, the URL no longer names a conversation, and the user can send

#### Scenario: New chat is not offered during a reply

- **WHEN** a reply is streaming
- **THEN** the New chat action is unavailable

### Requirement: Signing out removes the conversation from the URL

Signing out SHALL remove the conversation id from the URL. A session that ends by itself
SHALL keep it, so signing back in reopens the same conversation.

#### Scenario: The user signs out

- **WHEN** a user with an open conversation signs out
- **THEN** the URL no longer names a conversation

#### Scenario: The session ends by itself

- **WHEN** a user's session ends by itself while a conversation is open
- **AND** the user signs in again
- **THEN** the chat reopens that conversation with its history
