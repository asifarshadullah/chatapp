## Purpose

Decides which models can answer chat messages and which one does: a set of options from
configuration, each a service offering the OpenAI chat completions API, with one chosen by
default and the user able to pick another. The deployed app offers only its hosted model,
development offers the local one, and keys stay out of the repository and out of every answer.

## ADDED Requirements

### Requirement: Model options come from configuration

The system SHALL read its model options from configuration. Each option SHALL have an id, a
display name, a base URL, an API key and a model, and MAY have a reasoning effort. An option
SHALL be offered only when its base URL, model and API key are all set. One option SHALL be
named the default. The configuration shipped for deployment SHALL define only the hosted Groq
option and make it the default. The development configuration SHALL add the local Ollama option
and make it the default. The Groq option SHALL be offered in development only when a key for it
is set outside the repository.

#### Scenario: The deployed app offers only Groq

- **WHEN** the app runs in Production with the deployment configuration and a Groq key
- **THEN** the only option offered is Groq, and it is the default
- **AND** no local option is offered

#### Scenario: Development defaults to the local model

- **WHEN** the app runs in Development with no Groq key set
- **THEN** the only option offered is the local model, and it is the default

#### Scenario: Development offers Groq when a key is set

- **WHEN** the app runs in Development with a Groq key set in user secrets or an environment
  variable
- **THEN** both the local model and Groq are offered, and the local model is the default

#### Scenario: An option without a key is not offered

- **WHEN** an option is configured without an API key
- **THEN** it is not offered, and a message naming it is refused

### Requirement: The offered options can be listed

The system SHALL let a signed-in user list the offered options, each with its id and display
name, and SHALL say which is the default. The list SHALL NOT reveal any base URL, model
service or key.

#### Scenario: Listing the options

- **WHEN** a signed-in user asks for the model options
- **THEN** the response holds each offered option's id and display name, and the default's id
- **AND** it contains no base URL and no key

#### Scenario: Listing requires signing in

- **WHEN** a caller who is not signed in asks for the model options
- **THEN** the response is 401

### Requirement: A message is answered by the option it names

A message MAY name an option by id. The system SHALL answer it with that option when it is
offered, and with the default when the message names none. A message naming an option that is
not offered SHALL be refused before the stream starts with 400 Bad Request and the error
"That model is not available.".

#### Scenario: A message names an offered option

- **WHEN** a user sends a message naming an offered option
- **THEN** the reply comes from that option's service and model

#### Scenario: A message names no option

- **WHEN** a user sends a message without naming an option
- **THEN** the reply comes from the default option

#### Scenario: A message names an option that is not offered

- **WHEN** a user sends a message naming an option that is unknown or not offered, such as
  `local` on the deployed app
- **THEN** the system refuses it with 400 and the error "That model is not available."
- **AND** it is not an event stream, the message is not saved, and no model is asked

### Requirement: Replies come from the chosen option's chat completions endpoint

The system SHALL generate a reply by streaming a chat completion from the chosen option's base
URL, using its model and API key. The request SHALL carry the configured system prompt first,
when one is set, and then the conversation's most recent messages in order with their roles, as
limited by the history window. Each piece of text the service streams SHALL reach the caller as
a token as it arrives. When the option has a reasoning effort, the request SHALL ask for it; when
it has none, the request SHALL not mention one.

#### Scenario: A reply is streamed from the chosen service

- **WHEN** a user's message is answered by an option
- **THEN** the system requests a streamed chat completion from that option's base URL for its
  model, authenticated with its key
- **AND** the request carries the system prompt, then the conversation's most recent messages
  in order
- **AND** each piece of text the service streams reaches the caller as a token, in order

#### Scenario: Adding or switching a provider is a configuration change

- **WHEN** an option is added or changed to another service offering the same API
- **THEN** replies through that option come from that service with no change to the application

#### Scenario: An option's reasoning effort is requested

- **WHEN** the chosen option has a reasoning effort
- **THEN** the request asks the model for that reasoning effort

#### Scenario: An option without a reasoning effort

- **WHEN** the chosen option has no reasoning effort
- **THEN** the request does not mention a reasoning effort

### Requirement: Each request is bounded in history and reply length

So that a request fits a free tier's per-minute token limit however long the conversation
grows, the system SHALL send the model only the configured number of most recent messages, 10
by default, and SHALL ask for a reply of at most the configured number of tokens, 800 by
default. The latest user message SHALL always be sent. The saved conversation SHALL keep every
message, and the user SHALL still see all of them.

#### Scenario: A long conversation sends only its latest messages

- **WHEN** a user sends a message in a conversation that then holds 25 messages
- **THEN** the request carries the system prompt and the 10 most recent messages, in order,
  ending with the new one
- **AND** the conversation's history still holds all 25 messages

#### Scenario: A short conversation sends every message

- **WHEN** a conversation holds fewer messages than the history window
- **THEN** the request carries all of them

#### Scenario: The reply length is capped

- **WHEN** a request is sent
- **THEN** it asks for a reply of at most the configured number of tokens

### Requirement: The app refuses to start without a usable model configuration

The system SHALL refuse to start when no option is offered, when the default names an option
that is not offered, when an offered option's base URL is not an absolute HTTP or HTTPS URL,
when the history window or the reply cap is not a positive whole number, or when an option's
reasoning effort is set to anything but `low`, `medium` or `high`. The failure SHALL name the
setting.

#### Scenario: A deployment without the Groq key is refused

- **WHEN** the app starts in Production with no Groq key, so that no option is offered
- **THEN** it does not start
- **AND** the error says that no model option is offered

#### Scenario: A default that is not offered is refused

- **WHEN** the default names an option that is not configured or has no key
- **THEN** it does not start
- **AND** the error names the default setting

#### Scenario: A base URL that is not a web address is refused

- **WHEN** an offered option's base URL is not an absolute HTTP or HTTPS URL
- **THEN** it does not start
- **AND** the error names that option

#### Scenario: An unusable bound or reasoning effort is refused

- **WHEN** the app starts with a history window or reply cap of zero or less, or an option
  whose reasoning effort is `fast`
- **THEN** it does not start

### Requirement: API keys are never disclosed

The system SHALL NOT include any option's API key in any response, stream event or log entry,
including the options list and when a model service fails.

#### Scenario: A failing model service does not reveal the key

- **WHEN** a model service rejects or fails a request
- **THEN** neither the response nor the stream nor the logs contain the key
