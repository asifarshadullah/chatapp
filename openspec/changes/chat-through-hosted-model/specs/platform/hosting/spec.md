## ADDED Requirements

### Requirement: Secrets can be supplied as files

The app SHALL read settings from files in the host's secrets directory, `/etc/secrets` unless
configured otherwise. Each file's name SHALL name the setting, using `__` between sections as
environment variables do, and its content SHALL be the value. A setting supplied as a file SHALL
take precedence over the same setting supplied as an environment variable. The app SHALL start
normally when the directory does not exist.

#### Scenario: A secret file supplies a setting

- **WHEN** the secrets directory holds a file named `Ai__Models__groq__ApiKey` whose content is
  a key
- **THEN** the app uses that key for the Groq option

#### Scenario: A secret file wins over an environment variable

- **WHEN** a setting is supplied both as a secret file and as an environment variable
- **THEN** the app uses the file's value

#### Scenario: No secrets directory

- **WHEN** the secrets directory does not exist, as in development and the compose run
- **THEN** the app starts and reads its settings from the usual sources
