## MODIFIED Requirements

### Requirement: New chat starts an empty conversation

The chat SHALL always show a New chat action at the top of the conversation sidebar. It SHALL
be available whenever the chat shows messages or an error, and unavailable while the chat is
empty with no error and while a reply is streaming. It SHALL clear the shown messages and
any error and remove the id from the URL, so the next message starts a new conversation. The
previous conversation SHALL stay reachable through its URL and the sidebar.

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

#### Scenario: New chat on an empty chat

- **WHEN** the chat is empty and shows no error
- **THEN** the New chat action is shown but unavailable
