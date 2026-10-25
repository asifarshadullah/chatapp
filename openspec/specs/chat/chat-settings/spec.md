# Chat Settings Specification

## Purpose

Gives the chat a Settings dialog where the user chooses how the chat behaves, starting with the
model that answers, and remembers the choice in the browser.

## Requirements

### Requirement: The chat has a Settings dialog with a model choice

The chat SHALL offer a Settings button in its header that opens a Settings dialog. The dialog's
first field SHALL be Model, a dropdown listing the options the app offers by display name, with
the option in use selected. The dropdown SHALL be shown even when only one option is offered.
Saving SHALL apply the choice; cancelling SHALL leave the choice as it was.

#### Scenario: Opening the dialog

- **WHEN** a signed-in user clicks the Settings button
- **THEN** a Settings dialog opens with a Model dropdown listing every offered option by name
- **AND** the option in use is selected

#### Scenario: Only one option is offered

- **WHEN** the app offers a single option, as the deployed app does with Groq
- **THEN** the Model dropdown is shown with that option selected

#### Scenario: Saving a different model

- **WHEN** the user picks another option and saves
- **THEN** the dialog closes and the next message the user sends is answered by that option

#### Scenario: Cancelling

- **WHEN** the user picks another option and cancels
- **THEN** the dialog closes and the option in use is unchanged

#### Scenario: The options cannot be loaded

- **WHEN** the list of options fails to load
- **THEN** the dialog says the models could not be loaded
- **AND** messages are still sent, answered by the app's default

### Requirement: The chosen model is remembered in the browser

The chat SHALL use the app's default option until the user saves another, and SHALL remember a
saved choice in the browser across reloads and sign-ins. A remembered choice that the app no
longer offers SHALL be dropped in favour of the default. Each message SHALL name the option in
use.

#### Scenario: First use

- **WHEN** a user who has never saved a model opens the chat
- **THEN** the default option is in use

#### Scenario: The choice survives a reload

- **WHEN** a user saves a model and reloads the page
- **THEN** that model is still in use

#### Scenario: A remembered choice that is no longer offered

- **WHEN** the remembered option is not among the options the app offers, for example after
  moving from development to the deployed app
- **THEN** the default option is in use and the remembered choice is dropped

#### Scenario: Messages name the model in use

- **WHEN** the user sends a message
- **THEN** the message names the option in use
