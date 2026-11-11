# Sync billing by re-reading the subscription from Stripe

- Status: accepted
- Date: 2026-11-10
- Issue: [#30](https://github.com/asifarshadullah/chatapp/issues/30)

## Context and Problem Statement

A user upgrades to Pro on Stripe's hosted checkout, and Stripe then tells the app what happened
through webhooks. The app must keep one record per user that says whether they are on Pro, and
the daily message limit follows that record.

Webhooks are not a tidy stream of facts. Stripe does not promise the order it delivers them in,
and it retries any delivery that fails, so one event can arrive several times. One upgrade sends
several related events: the checkout completes, the subscription is created and updated, and an
invoice is paid. An invoice event can arrive before the checkout event that ties the subscription
to a user. A late `customer.subscription.deleted` for an old subscription can arrive after the
user has already started a new one.

The question this settles: how does each event change the stored subscription?

## Considered Options

- **Re-read the subscription.** Every relevant event only says "look again": the handler takes
  the subscription id from the event, asks Stripe for that subscription as it is now, and stores
  that.
- **Apply each event's payload.** Store what the event carries, comparing timestamps to throw
  away older news.
- **Handle only `customer.subscription.*` events.** Use the subscription object those events
  carry and ignore checkout and invoice events.

## Decision Outcome

Chosen option: **re-read the subscription**, because it makes order and repeats irrelevant.
Each sync stores Stripe's current state, so a late or repeated event finds the same answer as
a timely one, and an event that arrives before the user is known simply reads a subscription
that already names its user.

The user travels with the subscription itself: checkout sets `userId` in the subscription's
metadata, so every later read names the user, whichever event triggered it. The plan comes from
the subscription's price, mapped through configuration, so the domain never sees a Stripe price
id.

Applying payloads was rejected because it needs timestamps compared per field, and a payment
event that arrives before the checkout event would still find no record to update. Handling
only subscription events was rejected because it is just as order-sensitive, and it gives up the
checkout event that ties the user to the subscription.

Two rules keep the stored record consistent:

- **One record per user**, enforced by a unique index on the user id. Each sync writes with a
  single upsert filtered on the user, so two events for a new subscription arriving at once
  cannot create two records.
- **The latest subscription decides.** A different subscription replaces the stored one only if
  it grants Pro, or the stored one does not. A late `deleted` event for an old subscription
  therefore cannot take Pro away from a new one.

### Consequences

- Good, because handling an event is idempotent and order-free, and a test can deliver events
  in any order, or twice, and assert the same result.
- Good, because the stored record is always something Stripe actually said, never a merge of
  partial payloads.
- Bad, because each event costs one Stripe API call. That is a handful of calls per upgrade.
- Bad, because the webhook depends on Stripe's API being reachable. If the read fails, the
  handler answers 500 and Stripe retries the delivery later.
- Accepted: two different subscriptions for one user synced at the same moment could still be
  stored in the wrong order. That needs two live subscriptions at once, which the app prevents
  by refusing a second checkout with 409.
