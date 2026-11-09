## Purpose

Defines the plans a user can be on, Free and Pro, what each one gives, and how they are kept
in the database and listed for the Manage Plan page.

## ADDED Requirements

### Requirement: Two plans exist, Free and Pro

The system SHALL offer exactly two plans, Free and Pro. Each plan SHALL have a name, a price
per month and a daily message limit, all read from configuration. By default Free SHALL cost
nothing and allow 10 messages a day, and Pro SHALL cost 19 a month and allow 100. Every user
without a paid subscription SHALL be on Free.

#### Scenario: The default plans

- **WHEN** the app runs with the default configuration
- **THEN** Free costs 0 and allows 10 messages a day
- **AND** Pro costs 19 a month and allows 100 messages a day

#### Scenario: A new user is on Free

- **WHEN** a user who has never subscribed opens Manage Plan
- **THEN** their current plan is Free

### Requirement: The plans are written to the database at startup

The system SHALL write both plans to the database at startup, creating any plan that is
missing and updating an existing plan to match configuration. A plan SHALL keep its id across
restarts. Running the step again SHALL change nothing. Any other plan record SHALL no longer
be offered.

#### Scenario: A fresh database gets both plans

- **WHEN** the app starts against a database with no plans
- **THEN** Free and Pro exist afterwards

#### Scenario: A changed limit reaches the database

- **WHEN** Pro's daily limit is changed in configuration and the app restarts
- **THEN** the stored Pro plan has the new limit and the same id as before

#### Scenario: Restarting changes nothing

- **WHEN** the app starts twice with the same configuration
- **THEN** there are still exactly two plans, with the same ids

### Requirement: The app refuses plan settings that make no sense

The system SHALL refuse to start when a plan's daily message limit is not a positive whole
number or its price is negative.

#### Scenario: A limit of zero is refused

- **WHEN** the app starts with Free's daily limit set to 0
- **THEN** it does not start

### Requirement: Listed plans show only what they give

The plan list SHALL return each plan's id, name, tier, price per month and daily message
limit, Free first. The Manage Plan page SHALL describe each plan by its daily message limit,
for example "100 messages a day", and SHALL NOT list features the app does not have.

#### Scenario: Listing the plans

- **WHEN** a signed-in user requests the plan list
- **THEN** it holds Free and then Pro, each with its id, name, tier, price and daily limit

#### Scenario: The page describes Pro by its allowance

- **WHEN** a user opens Manage Plan
- **THEN** Pro is described as "100 messages a day"
- **AND** neither document upload nor sharing is listed
