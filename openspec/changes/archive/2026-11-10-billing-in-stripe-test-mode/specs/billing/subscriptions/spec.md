## Purpose

Keeps each user's stored subscription in step with Stripe, so paying makes the user Pro and a
cancelled or failed subscription returns them to Free, and shows the user their current plan.

## ADDED Requirements

### Requirement: Stripe's events bring the stored subscription up to date

The system SHALL accept Stripe events at its webhook endpoint only with a valid Stripe
signature. A request with a missing or bad signature SHALL be answered 400 and SHALL change
nothing. For a completed checkout and for every event about a subscription or its invoice,
the system SHALL read that subscription's current state from Stripe and store it for the user
the checkout was tied to: its plan, its status, the end of its current period and whether it
ends then. Events SHALL give the same result whatever order they arrive in, however often
each arrives, and when several arrive at the same moment. A user SHALL never have more than
one stored subscription. Events of other types, events about a subscription Stripe no longer
knows, and subscriptions not tied to a user or not for Pro SHALL be acknowledged and change
nothing. When Stripe cannot be reached to read the subscription, the system SHALL answer
with a server error, so Stripe delivers the event again later.

#### Scenario: Paying makes the user Pro

- **WHEN** Stripe reports a completed checkout for a Free user's Pro subscription
- **THEN** that user's current plan is Pro, with the period end Stripe reports

#### Scenario: Events out of order

- **WHEN** Stripe reports the first invoice paid before it reports the completed checkout
- **THEN** the user ends up on Pro, as when the events arrive in order

#### Scenario: The same event twice

- **WHEN** Stripe delivers the same event twice
- **THEN** the stored subscription is the same as after the first delivery

#### Scenario: Two events at the same moment

- **WHEN** Stripe delivers the completed checkout and the first paid invoice for a new
  subscription at the same moment
- **THEN** the user has exactly one stored subscription, and it is Pro

#### Scenario: Stripe is unreachable

- **WHEN** an event arrives and Stripe cannot be reached to read the subscription
- **THEN** the system answers with a server error and changes nothing

#### Scenario: A forged event

- **WHEN** a request reaches the webhook endpoint with a bad signature
- **THEN** the system answers 400 and no subscription changes

### Requirement: The current plan follows the subscription's status

A user SHALL be on Pro while their subscription is active or trialing, including after they
have asked to cancel and before the period ends. A user SHALL be on Free when they have no
subscription, or when it is cancelled, past due, unpaid or incomplete. A user's latest
subscription SHALL decide their plan: an event about an older subscription SHALL NOT take
Pro away from a user whose latest subscription is active.

#### Scenario: A failed renewal returns the user to Free

- **WHEN** Stripe reports that a Pro user's renewal payment failed and the subscription is
  past due
- **THEN** the user is on Free

#### Scenario: A late event about an old subscription

- **WHEN** a user on an active Pro subscription receives a late event saying an earlier
  subscription of theirs was cancelled
- **THEN** the user stays on Pro

### Requirement: Cancelling keeps Pro until the period ends

When a Pro user cancels, the system SHALL ask Stripe to end the subscription at the end of the
current period, and the user SHALL stay on Pro until then. When Stripe reports the
subscription ended, the user SHALL be on Free. A user on Free SHALL get 404 when cancelling.

#### Scenario: Cancelling

- **WHEN** a Pro user whose period ends on 30 November cancels on 10 November
- **THEN** they are still on Pro, and Manage Plan says Pro ends on 30 November
- **AND** the cancel button is no longer offered

#### Scenario: The period ends

- **WHEN** Stripe reports that a cancelled subscription has ended
- **THEN** the user is on Free

### Requirement: The user can see their current plan

The current-subscription endpoint SHALL always answer a signed-in user with their current
plan's name and tier. For Pro it SHALL also give the end of the current period, and whether
the subscription renews then or ends then. Manage Plan SHALL show the current plan by name,
mark that plan's card as current, and show "Renews on <date>" or "Ends on <date>" for Pro.

#### Scenario: A Free user

- **WHEN** a user with no subscription requests their current subscription
- **THEN** the answer is Free, with no period end

#### Scenario: A Pro user

- **WHEN** a Pro user opens Manage Plan
- **THEN** the page shows Pro as the current plan with its renewal date, and the Pro card is
  marked current
