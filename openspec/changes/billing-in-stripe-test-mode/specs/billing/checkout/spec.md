## Purpose

Lets a user on Free start an upgrade to Pro through Stripe's hosted checkout, keeps the app in
Stripe's test mode so no real money moves, and says clearly when upgrades are not available.

## ADDED Requirements

### Requirement: Checkout is available only with a complete Stripe setup

The system SHALL consider checkout available only when a Stripe secret key, a webhook signing
secret and a price for Pro are all configured, and none of them is a placeholder. The system
SHALL report the checkout mode to signed-in users as `test` when the key is a test key, `live`
when it is a live key, and `off` when checkout is not available.

#### Scenario: A test setup

- **WHEN** a test secret key, a webhook secret and a Pro price are configured
- **THEN** the checkout mode is `test`

#### Scenario: A missing webhook secret turns checkout off

- **WHEN** a test secret key and a Pro price are configured but the webhook secret is not
- **THEN** the checkout mode is `off`

#### Scenario: The shipped placeholders turn checkout off

- **WHEN** the app runs with the placeholder Stripe settings from the repository
- **THEN** the checkout mode is `off`

### Requirement: A live Stripe key needs an explicit opt-in

The system SHALL refuse to start when the configured Stripe secret key is a live key, unless
a setting that allows live keys is turned on. A test key or no key SHALL never stop the app.

#### Scenario: A live key without the opt-in

- **WHEN** the app starts with a live Stripe key and live keys are not allowed
- **THEN** it does not start, and the error names the setting that allows live keys

#### Scenario: A live key with the opt-in

- **WHEN** the app starts with a live Stripe key and live keys are allowed
- **THEN** it starts and the checkout mode is `live`

### Requirement: Starting a checkout

When checkout is available, a user on Free who chooses Subscribe on Pro SHALL be sent to a
Stripe hosted checkout for Pro, which returns to the app's `/billing/success` when paid and
`/billing/cancel` when abandoned. The checkout SHALL be tied to the user, so its result
reaches their account. The system SHALL refuse to start a checkout:

- with 503 and the message "Upgrades are turned off on this demo. No payment was taken."
  when checkout is not available, without contacting Stripe;
- with 409 when the user is already on Pro, including when their Pro ends at the period's end;
- with 404 when the plan is unknown, and with 400 when the plan is Free;
- with 502 and the message "The upgrade could not be started. Please try again later." when
  Stripe refuses to create the checkout.

#### Scenario: A Free user starts a checkout

- **WHEN** a user on Free subscribes to Pro while checkout is available
- **THEN** the response carries a Stripe checkout URL and the page goes there

#### Scenario: Checkout is off

- **WHEN** a user subscribes to Pro while checkout is not available
- **THEN** the system answers 503 with "Upgrades are turned off on this demo. No payment was
  taken."
- **AND** Stripe is not contacted

#### Scenario: A Pro user cannot pay twice

- **WHEN** a user already on Pro subscribes to Pro
- **THEN** the system answers 409 and no checkout is started

#### Scenario: Stripe refuses the checkout

- **WHEN** a Free user subscribes to Pro and Stripe rejects the request, for example because
  the configured price does not exist
- **THEN** the system answers 502 with "The upgrade could not be started. Please try again
  later."

### Requirement: The Manage Plan page tells the user what upgrading means here

The Manage Plan page SHALL show, above the plans:

- in `test` mode, that this is a demo in Stripe's test mode, that card 4242 4242 4242 4242
  with any future date and any CVC can be used, and that nothing is charged;
- in `off` mode, that upgrades are turned off here, with Subscribe disabled.

When starting a checkout fails, the page SHALL stay where it is and show the server's message
for a 503, or "The upgrade could not be started. Please try again later." for any other
failure. Trying again SHALL clear the earlier message. When the plans, the current plan or the
checkout mode cannot be loaded, the page SHALL say "Manage Plan could not be loaded. Please
try again later." instead of loading forever, and SHALL keep the way back to chat.

#### Scenario: The demo banner

- **WHEN** a user opens Manage Plan while the checkout mode is `test`
- **THEN** the page shows the test-mode notice with the test card number

#### Scenario: Upgrades are off

- **WHEN** a user opens Manage Plan while the checkout mode is `off`
- **THEN** the page says upgrades are turned off and Subscribe is disabled

#### Scenario: A failed start is shown

- **WHEN** starting a checkout fails with a server error
- **THEN** the page shows "The upgrade could not be started. Please try again later." and
  stays on Manage Plan

#### Scenario: The page cannot load

- **WHEN** a user opens Manage Plan and the plan list request fails
- **THEN** the page says "Manage Plan could not be loaded. Please try again later." and offers
  the way back to chat

### Requirement: Returning from Stripe opens Manage Plan

When the app is opened at `/billing/success` or `/billing/cancel`, it SHALL show Manage Plan,
once the user is signed in, and replace the address with the app's root. After
`/billing/success` it SHALL say the payment was received and the plan updates once Stripe
confirms it, and SHALL check the current plan again every few seconds for up to 30 seconds,
until it is Pro. If the plan is still not Pro after that, it SHALL say the plan has not
updated yet and to reload the page in a minute. After `/billing/cancel` it SHALL say that
checkout was cancelled and nothing was charged. Stripe SHALL return the user to the address
they started from, the dev server included.

#### Scenario: Returning after paying

- **WHEN** a user returns to `/billing/success` and Stripe's confirmation arrives a few
  seconds later
- **THEN** Manage Plan first says the plan will update, then shows Pro as the current plan

#### Scenario: Stripe's confirmation is late

- **WHEN** a user returns to `/billing/success` and no confirmation arrives within 30 seconds
- **THEN** Manage Plan says the plan has not updated yet and to reload the page in a minute

#### Scenario: Returning after cancelling checkout

- **WHEN** a user returns to `/billing/cancel`
- **THEN** Manage Plan says checkout was cancelled and nothing was charged
- **AND** the current plan is unchanged
