## Purpose

Controls how a build reaches the live host: a deploy happens only when someone starts it, from
the branch they choose, and only after the tests pass on that branch.

## ADDED Requirements

### Requirement: Deploys start only by hand, from a chosen branch

A deploy SHALL happen only when a person with write access to the repository starts the deploy
workflow, and it SHALL deploy the commit at the head of the branch they choose. Pushing,
opening a pull request or merging SHALL NOT deploy anything.

#### Scenario: Merging to main deploys nothing

- **WHEN** a pull request is merged into `main`
- **THEN** the live app is unchanged

#### Scenario: Deploying a chosen branch

- **WHEN** the owner starts the deploy workflow on a branch
- **THEN** the live app runs the commit at the head of that branch once the deploy finishes

### Requirement: Failing tests block the deploy

A deploy SHALL run the same backend and frontend checks as CI against the chosen commit, and
SHALL NOT publish an image or ask the host to deploy unless all of them pass.

#### Scenario: A failing test stops the deploy

- **WHEN** a backend or frontend check fails during a deploy
- **THEN** no image is published for that deploy
- **AND** the host is not asked to deploy
- **AND** the live app is unchanged

#### Scenario: Passing checks lead to a deploy

- **WHEN** every check passes during a deploy
- **THEN** an image is published and the host is asked to deploy it

### Requirement: The live app runs the image built from the deployed commit

Each deploy SHALL build the image from the chosen commit, publish it under a tag naming that
commit, and ask the host to run exactly that tag.

#### Scenario: The deployed image names its commit

- **WHEN** a deploy of a commit finishes
- **THEN** the live app runs the image tagged with that commit

### Requirement: CI builds the production image

CI SHALL build the production image on every push and pull request, without publishing it, so a
change that breaks the image fails CI.

#### Scenario: A broken image fails CI

- **WHEN** a pull request changes something that stops the image from building
- **THEN** CI fails for that pull request

### Requirement: No deployment secret is stored in the repository or the image

Secrets the live app needs, such as the database connection string and the token signing
secret, SHALL be supplied by the host at runtime. The repository and the published image SHALL
NOT contain them.

#### Scenario: The live app gets its secrets from the host

- **WHEN** the live app starts
- **THEN** it reads its connection string and signing secret from the host's environment
- **AND** neither value appears in the repository or the published image
