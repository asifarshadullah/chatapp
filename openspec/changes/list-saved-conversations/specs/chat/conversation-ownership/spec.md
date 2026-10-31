## ADDED Requirements

### Requirement: Only the caller's own conversations are listed

The conversation list SHALL hold only conversations the caller owns. No role or permission
SHALL add another user's conversation to it, and nothing in the list SHALL reveal whether
other users have conversations.

#### Scenario: Two users list their conversations

- **WHEN** user A and user B each have conversations and A requests the list
- **THEN** the list holds every conversation of A's and none of B's

#### Scenario: An administrator lists conversations

- **WHEN** a user whose role grants every permission requests the list
- **THEN** the list holds only conversations they own
