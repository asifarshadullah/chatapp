## Why

Billing is one of the app's three bounded contexts, but it does not work end to end (#30). No plan records exist, so the Manage Plan page offers nothing. Checkout sends a user to Stripe, but nothing records the subscription afterwards, so a user who pays stays on Free. A paid plan unlocks nothing that exists, and on the live demo a failed checkout start shows nothing at all. The demo should let a reviewer upgrade for real, through Stripe, without any real money moving, and Stripe's test mode is made for exactly that.

## What Changes

- **Two plans, Free and Pro, defined in configuration and written to the database at startup.** Enterprise is dropped. Each plan shows its price and its real allowance, "10 messages a day" or "100 messages a day". Document Upload and Share are no longer listed, since neither exists.
- **The daily message limit follows the user's plan.** Free allows 10 messages a day and Pro 100. A Free user who reaches the limit is told that Pro allows more and where to upgrade. **BREAKING:** the single `Chat:DailyMessageLimit` setting is replaced by each plan's limit.
- **Completing checkout makes the user Pro.** Every Stripe event about a subscription brings the stored subscription up to date from Stripe's current state, so the events can arrive in any order or more than once. A webhook with a bad signature changes nothing.
- **The Manage Plan page shows the real current plan,** its renewal date, or the date it ends after a cancellation, and marks the current plan.
- **Cancelling keeps Pro until the paid period ends,** then the user is back on Free. Today it ends the subscription at Stripe immediately.
- **Stripe runs in test mode only.**
  - The app refuses to start with a live Stripe key unless a setting explicitly allows it.
  - With a test key, the Manage Plan page says it is a demo and which test card to use.
  - Without a complete Stripe setup, the page says upgrades are turned off, Subscribe is disabled, and the server refuses a checkout with 503. Any other failure to start a checkout is shown as a general message instead of being swallowed.
- **A user already on Pro cannot start a second checkout.** If Stripe refuses to create a checkout, the user gets a clear failure instead of a server error.
- **Manage Plan no longer spins forever.** When the page cannot load, it says so and offers the way back to chat.
- **Returning from Stripe opens Manage Plan.** After a payment it says the plan updates once Stripe confirms it, and checks again until it does. After a cancelled checkout it says nothing was charged.
- **Local development** forwards `/billing` to the backend, so Manage Plan loads under the Vite dev server.
- **Deployment:** the Stripe test key, webhook secret and Pro price id are supplied as Render Secret Files. A Stripe webhook is registered for the live URL. The README explains how to try upgrading on the demo.

## Non-goals

- **Demo upgrades ending by themselves.** A user can cancel, and Groq's shared daily allowance still bounds use. If test subscriptions build up, expiry gets its own issue.
- **Stripe's customer portal, changing between paid plans, proration, and Enterprise.**
- **Resuming a cancelled subscription before its period ends.** The user stays on Pro until then and can subscribe again afterwards.
- **Removing the unused `DocumentUpload` and `Share` feature values from the code.** They are only no longer offered.

## Capabilities

### New Capabilities
- `billing/plans`: which plans exist, what each offers, how they are written at startup, and how they are listed.
- `billing/checkout`: starting a checkout, the test-mode guard, the "turned off" state, and returning from Stripe.
- `billing/subscriptions`: how Stripe events update the stored subscription, what the current plan is, and cancelling at the end of the period.

### Modified Capabilities
- `chat/daily-message-limit`: the limit comes from the user's plan instead of one configured number, and the refusal tells a Free user that Pro allows more.

## Impact

- **Backend:**
  - **Chat.Billing.Domain:** `Plan` gains a daily message limit. `Subscription` gains whether it ends at the period's end, and one way to take Stripe's state.
  - **Chat.Billing.Application:**
    - `IPaymentGateway` gains reading a subscription from Stripe and cancelling at the period's end.
    - `ISubscriptionService` replaces its three event handlers with one sync step, and returns the current plan.
    - `IPlanFeatureService` gains the user's daily message limit.
  - **Chat.Billing.Infrastructure:**
    - `StripeSettings` gains a live-key opt-in and an "is checkout available" rule.
    - `StripePaymentGateway` attaches the user to the subscription and reads subscriptions back.
    - `StripeWebhookHandler` handles the checkout and subscription events and passes each to the sync.
    - A plan seeder and the Mongo repositories change to match. The subscription is stored with one atomic upsert per user, behind a unique index.
    - The billing documents store their ids as strings, like the other contexts.
  - **Chat.Application:** `IDailyMessageQuota` takes the limit per call. A new `IDailyMessageAllowance` port supplies the user's limit.
  - **Chat.Api:**
    - `BillingController` gains `GET /billing/checkout-mode`, refuses a checkout with 503 when Stripe is not set up and with 409 when the user is already on Pro, and always answers `GET /billing/subscription`.
    - A small adapter connects the chat's allowance port to Billing.
    - Startup validation covers the live-key guard and the plan limits.
- **Data:**
  - The `plans` collection gets its two records at startup, updated to match configuration.
  - The `subscriptions` collection gains a field for ending at the period's end, and a unique index on the user.
  - Both collections are empty on the live demo today, so the change of id format needs no migration.
- **Frontend:**
  - `BillingPage` shows the mode banner, the current plan, its dates and messages.
  - `billingService` gains the mode and the new subscription shape.
  - `App` handles the return from Stripe.
  - `vite.config.ts` gains the `/billing` proxy, keeping the browser's host so Stripe returns to the dev server.
- **Configuration:**
  - `Billing:Plans` replaces `Chat:DailyMessageLimit`; Development sets both plans high so the end-to-end tests are not limited.
  - New `Stripe:AllowLiveKey`. The Stripe settings drop the Enterprise price.
- **Operations:**
  - Three Render Secret Files for Stripe, and a webhook endpoint in the Stripe dashboard.
  - Pro's daily limit of 100 matches the whole demo's shared Groq allowance, so in practice the limit a reviewer feels is Free's.
