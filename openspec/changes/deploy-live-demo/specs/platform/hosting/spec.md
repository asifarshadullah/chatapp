## Purpose

Lets the app run outside a developer machine: one process serves the UI and the API from a
single origin behind the host's TLS proxy, reports its health, and refuses to start with a
signing secret anyone could know.

## ADDED Requirements

### Requirement: One origin serves the app and the API

The deployed app SHALL serve the built UI and the API from the same origin. A request for a
path that is neither an API route nor a file SHALL receive the UI's entry page, so a link into
the app loads it. A request for an unknown path under `/api` or `/auth` SHALL receive 404, not
the entry page.

#### Scenario: Opening the site loads the app

- **WHEN** a browser requests `/`
- **THEN** the response is the UI's entry page

#### Scenario: A link into the app loads the app

- **WHEN** a browser requests a path such as `/billing/success` that is not an API route or a
  file
- **THEN** the response is the UI's entry page

#### Scenario: The UI's assets are served

- **WHEN** the entry page requests one of the UI's built scripts or stylesheets
- **THEN** that file is served

#### Scenario: An unknown API path is not found

- **WHEN** a client requests an unknown path under `/api` or `/auth`
- **THEN** the response is 404
- **AND** the body is not the UI's entry page

#### Scenario: The API still answers on the same origin

- **WHEN** the UI calls an existing API route, such as signing in
- **THEN** the API handles it as before

### Requirement: Behind a TLS proxy the app uses the public scheme and host

When the host's proxy forwards a request with headers naming the original scheme and host, the
app SHALL treat the request as made to that scheme and host. URLs the app builds for the browser
SHALL use them.

#### Scenario: A sign-in redirect uses the public HTTPS address

- **WHEN** a request forwarded as HTTPS for the public host starts an external sign-in
- **THEN** the callback address the app sends to the provider uses `https` and the public host

#### Scenario: A forwarded HTTPS request is not redirected

- **WHEN** a request forwarded as HTTPS reaches the app over plain HTTP from the proxy
- **THEN** the app serves it without redirecting

### Requirement: The app reports its health

The app SHALL answer a health request with success while it is running, without requiring the
caller to sign in, and the answer SHALL name the commit the running image was built from.

#### Scenario: The host checks the app's health

- **WHEN** an unauthenticated client requests the health endpoint of a running app
- **THEN** the response is 200

#### Scenario: The health answer names the running commit

- **WHEN** a client requests the health endpoint of an app built from a commit
- **THEN** the answer names that commit
- **AND** an app not built from a commit, such as one run with `dotnet run`, names none

### Requirement: The app refuses a signing secret anyone could know

Outside the Development environment, the app SHALL refuse to start when the token signing
secret is missing, shorter than 32 bytes, or the placeholder value committed to the repository.
The failure SHALL name the setting.

#### Scenario: The committed placeholder is refused

- **WHEN** the app starts in Production with the signing secret left at the committed
  placeholder
- **THEN** it does not start
- **AND** the error names the signing secret setting

#### Scenario: A short secret is refused

- **WHEN** the app starts in Production with a signing secret shorter than 32 bytes
- **THEN** it does not start

#### Scenario: A missing secret is refused

- **WHEN** the app starts in Production with no signing secret
- **THEN** it does not start

#### Scenario: A proper secret is accepted

- **WHEN** the app starts in Production with a signing secret of at least 32 bytes that is not
  the placeholder
- **THEN** it starts

#### Scenario: Development keeps its local secret

- **WHEN** the app starts in Development with the development settings
- **THEN** it starts

### Requirement: An unconfigured sign-in provider does not break the app

When no Google client id is configured, the app SHALL serve every other request normally, and
starting Google sign-in SHALL answer 404.

#### Scenario: The app works without Google configured

- **WHEN** the app runs without a Google client id and a client calls any other endpoint
- **THEN** that endpoint answers as it would with Google configured

#### Scenario: Google sign-in is not offered without its configuration

- **WHEN** the app runs without a Google client id and a client starts Google sign-in
- **THEN** the response is 404

### Requirement: No other origin is granted cross-origin access

The app SHALL NOT grant cross-origin access to any origin, with or without credentials, because
the UI reaches the API on its own origin.

#### Scenario: A page on another origin is not granted access

- **WHEN** a browser sends a cross-origin request or preflight to the API from another origin,
  including `http://localhost:5173`
- **THEN** the response carries no header granting that origin access

### Requirement: The production image runs locally

The same image that is deployed SHALL run on a developer machine with Docker, against the local
MongoDB and the host's Ollama, started by one command.

#### Scenario: Running the image locally

- **WHEN** a developer with Docker and Ollama running starts the local app profile
- **THEN** the app is served on a local port
- **AND** a user can sign up and chat there
