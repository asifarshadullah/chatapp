# Conversation Sidebar Specification

## Purpose

Shows a signed-in user their saved conversations beside the chat, so they can go back to any
of them without its link.

## Requirements

### Requirement: The chat shows the user's conversations in a sidebar

The chat SHALL show a sidebar listing the signed-in user's conversations by title, most
recently active first, with New chat at its top. On a window at least 900 pixels wide the
sidebar SHALL always be shown beside the chat. On a narrower window it SHALL be hidden until
the user opens it from a menu button, and SHALL then cover the chat until it is closed or a
conversation or New chat is chosen. Titles SHALL be shown as plain text. The sidebar SHALL
only ever show the conversations of the user currently signed in.

#### Scenario: A wide window

- **WHEN** a signed-in user with saved conversations opens the chat on a window 1280 pixels
  wide
- **THEN** the sidebar is shown beside the chat with their conversations' titles, most
  recently active first

#### Scenario: A narrow window

- **WHEN** a signed-in user opens the chat on a window 390 pixels wide
- **THEN** the sidebar is not shown and a menu button is
- **AND** choosing the menu button shows the sidebar

#### Scenario: A title that looks like markup

- **WHEN** a conversation's title is "<b>bold</b>"
- **THEN** the sidebar shows the text "<b>bold</b>", not bold text

#### Scenario: Another user signs in in the same tab

- **WHEN** user A signs out and user B signs in in the same tab
- **THEN** the sidebar shows only B's conversations, and none of A's at any point

### Requirement: The sidebar shows loading, empty and failed states

While the first page of the list loads, the sidebar SHALL show that it is loading. A user
with no conversations SHALL see "No conversations yet.". When the first page cannot be
loaded and no entries are shown, the sidebar SHALL show "Your conversations could not be
loaded." with a Retry action that loads it again, and the chat SHALL stay usable. When
loading any page finds that the session has ended, the user SHALL be sent to sign in.

#### Scenario: A user with no conversations

- **WHEN** a signed-in user with no conversations opens the chat
- **THEN** the sidebar shows "No conversations yet."

#### Scenario: The list cannot be loaded

- **WHEN** the chat opens and the server cannot be reached for the list
- **THEN** the sidebar shows "Your conversations could not be loaded." and a Retry action
- **AND** the user can still send a message
- **AND** choosing Retry once the server is reachable shows the list

#### Scenario: The session has ended

- **WHEN** loading the list finds that the user's session has ended
- **THEN** the user is sent to sign in

### Requirement: More conversations load on request

When the list has more conversations than its first page, the sidebar SHALL offer "Show
more", which adds the next page below the entries already shown. No conversation SHALL be
shown twice. "Show more" SHALL NOT be offered once the last page is shown. When the next page
cannot be loaded, the entries already shown SHALL stay, the sidebar SHALL show "More
conversations could not be loaded." and "Show more" SHALL stay offered, so choosing it again
retries.

#### Scenario: Showing more

- **WHEN** a user with 25 conversations opens the chat
- **THEN** the sidebar shows 20 conversations and "Show more"
- **AND** choosing "Show more" shows all 25, each once, and no longer offers "Show more"

#### Scenario: Showing more fails

- **WHEN** a user chooses "Show more" and the server cannot be reached
- **THEN** the 20 conversations already shown stay
- **AND** the sidebar shows "More conversations could not be loaded." and still offers
  "Show more"

### Requirement: Choosing a conversation opens it

Choosing a conversation in the sidebar SHALL open it as a URL naming it does: the URL names
it without adding a browser history entry, its saved messages are shown in order, and a
message the user then sends continues it. The open conversation's entry SHALL be marked as
the current one, and choosing it again SHALL leave the chat as it is. The sidebar's entries
SHALL be unavailable while a reply is streaming. On a narrow window, choosing a conversation
or New chat SHALL close the sidebar.

#### Scenario: Opening an earlier conversation

- **WHEN** a user with conversation A open chooses conversation B in the sidebar
- **THEN** the URL names B and the chat shows B's saved messages in order
- **AND** B's entry is marked as the current one
- **AND** a message the user then sends is added to B

#### Scenario: Choosing the open conversation

- **WHEN** a user chooses the entry of the conversation already open
- **THEN** the chat keeps showing it as it is, without loading it again

#### Scenario: Choosing during a reply

- **WHEN** a reply is streaming
- **THEN** the sidebar's conversations cannot be chosen

#### Scenario: Choosing on a narrow window

- **WHEN** a user on a narrow window opens the sidebar and chooses a conversation
- **THEN** the sidebar closes and the chat shows that conversation

#### Scenario: New chat on a narrow window

- **WHEN** a user on a narrow window with a conversation open opens the sidebar and chooses
  New chat
- **THEN** the sidebar closes and the chat is empty

### Requirement: The list follows the user's activity

When sending a message finishes, however it ends, the sidebar SHALL load the list's first
page again, so a new conversation appears at the top with its title and a continued one moves
to the top. Entries already loaded from later pages SHALL stay, each shown once. If that
reload fails, the entries already shown SHALL stay and no error SHALL be shown for it.

#### Scenario: Starting a conversation

- **WHEN** a user sends the first message of a new conversation and its reply streams to its
  end
- **THEN** the conversation appears at the top of the sidebar with its title, and is marked
  as the current one

#### Scenario: Continuing an older conversation

- **WHEN** a user opens a conversation that is not at the top of the sidebar and sends a
  message into it
- **THEN** once the reply ends, that conversation is at the top of the sidebar
- **AND** it is not shown twice

#### Scenario: The reload after a reply fails

- **WHEN** a reply ends and the server cannot be reached for the list
- **THEN** the sidebar keeps the entries it showed and shows no error
