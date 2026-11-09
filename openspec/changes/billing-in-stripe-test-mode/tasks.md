## 1. Plans from configuration

- [x] 1.1 Add a Mongo fixture to Chat.Billing.Tests like `Chat.Infrastructure.Tests`' `MongoDbFixture` (port 27018, its own database, dropped after the run). Verify with one test that inserts and reads back a document, with `docker compose up -d` running
- [x] 1.2 Write failing tests for the plan settings and `Plan` (plans "Two plans exist, Free and Pro", "The app refuses plan settings that make no sense"):
  - **Chat.Billing.Tests:** the defaults are Free at 0 with 10 a day and Pro at 19 with 100, and `Plan` carries a daily message limit.
  - **Chat.Identity.Tests startup tests:** Free's `DailyMessages` of 0, or a negative Pro price, stops the app with an error naming the setting.
- [x] 1.3 Add `BillingSettings` with `Plans:Free` and `Plans:Pro` (`Name`, `PricePerMonth`, `DailyMessages`), validated on start, and the limit on `Plan` and `PlanDocument`. Set both limits to 1000 in `appsettings.Development.json`. Verify 1.2 passes
- [x] 1.4 Write failing Mongo repository tests (design 4b and 12):
  - A plan and a subscription saved and read back keep every field.
  - Their ids are stored as strings, checked on the raw BSON.
  - A document with an extra unknown field still reads.
  - `GetByTierAsync` finds a plan by tier.
- [x] 1.5 Add `[BsonRepresentation(BsonType.String)]` to every `Guid` and `[BsonIgnoreExtraElements]` to `PlanDocument` and `SubscriptionDocument`, and add `GetByTierAsync`. Verify 1.4 passes
- [x] 1.6 Write failing Mongo tests for `PlanSeeder.SeedAsync` (plans "The plans are written to the database at startup"):
  - An empty collection gets Free and Pro.
  - A changed Pro limit is written, and Pro keeps its id.
  - A second run leaves exactly two records with the same ids.
  - A pre-existing Enterprise record is removed.
- [x] 1.7 Implement `PlanSeeder` and call it in the startup scope next to `RoleSeeder`. Verify 1.6 passes
- [x] 1.8 Write a failing API test (plans "Listed plans show only what they give"): `GET /billing/plans` returns Free then Pro, each with id, name, tier, price and daily limit. Add `DailyMessageLimit` to `PlanDto`, order by tier, and verify the test passes

## 2. The daily limit follows the plan

- [x] 2.1 Write failing tests (daily-message-limit "Each user may send a limited number of messages per day", "The limit is configurable and must be positive"):
  - **`MongoDailyMessageQuota`:** `TryConsumeAsync(user, limit)` counts against the limit passed in. After 10 messages at limit 10, the same user is accepted at limit 100, the same day.
  - **`ChatService`:**
    - the limit comes from `IDailyMessageAllowance`, which is not asked when the conversation is not found;
    - a refusal throws with the limit and the upgrade limit;
    - a refused message still saves nothing.
  - Adjust the existing quota tests and `ChatServiceTests`' `FakeQuota` to pass a limit.
- [x] 2.2 Add `IDailyMessageAllowance` and `DailyAllowance(Limit, UpgradeLimit)` to Chat.Application. Change `IDailyMessageQuota` to take the limit per call, and update `ChatService` and the exception. Remove `ChatSettings.DailyMessageLimit` and its validation. Verify 2.1 passes
- [x] 2.3 Write failing tests in Chat.Billing.Tests for `RealPlanFeatureService.GetMessageAllowanceAsync`:
  - No subscription gives Free's limit with Pro's as the upgrade.
  - Active and trialing give Pro's limit with no upgrade. The case of a subscription ending at the period's end is tested in 4.1, once that field exists.
  - Past due and cancelled give Free's.
- [x] 2.4 Rework the test doubles (design 11b):
  - `ChatApiFactory` gets a settable `FakeDailyMessageAllowance`, and `InMemoryDailyMessageQuota` counts against the limit it is given.
  - `ChatStreamingTests` sets `factory.Allowance.Limit`.
  - The `IPlanFeatureService` fakes in `AuthApiFactory` and `PlanFeatureFactories` implement the new method as unlimited.
  - `UnlimitedDailyMessageQuota` takes the limit.
  - `ModelWiringTests` checks that the allowance port resolves to the plan adapter, instead of `Chat:DailyMessageLimit`.
  - Verify the backend builds.
- [x] 2.5 Write failing API tests for the 429:
  - A Free user over the limit is told the limit, that it resets at midnight UTC, and that Pro allows 100 messages a day on the Manage Plan page.
  - A Pro user's message has no Pro sentence.
  - Replace the `Chat:DailyMessageLimit` startup test with the plan-limit one from 1.2.
- [x] 2.6 Implement `GetMessageAllowanceAsync`, the `PlanDailyMessageAllowance` adapter in Chat.Api, and the controller's message, and register them. Verify 2.3 and 2.5 pass and every existing backend test still passes

## 3. Checkout mode and starting a checkout

- [x] 3.1 Write failing tests for `StripeSettings.Mode` (checkout "Checkout is available only with a complete Stripe setup"):
  - Test values for all three settings give `test`.
  - A missing webhook secret gives `off`.
  - The shipped placeholders give `off`.
  - Empty values give `off`.
  - `sk_live_` and `rk_live_` keys give `live`.
- [x] 3.2 Write failing startup tests (checkout "A live Stripe key needs an explicit opt-in"):
  - A live key without `Stripe:AllowLiveKey` stops the app with an error naming that setting.
  - With it, the app starts.
  - A test key or no key starts.
- [x] 3.3 Implement `Mode`, `AllowLiveKey` and the start-up check, and drop `PriceIds:Enterprise` from `appsettings.json`. Verify 3.1 and 3.2 pass
- [x] 3.4 Rework the existing uncommitted `BillingSubscribeTests` into failing tests for checkout "Starting a checkout", using a fake `IPaymentGateway` and a fake subscription lookup:
  - `GET /billing/checkout-mode` returns the mode.
  - With the mode `off`, Subscribe answers 503 with the message, without reading plans or calling the gateway.
  - With a test setup, Subscribe answers:
    - 404 for an unknown plan and 400 for Free;
    - 409 for a user on Pro, and for one whose Pro ends at the period's end;
    - 502 with the general message when the gateway throws a `StripeException`.
  - For a Free user subscribing to Pro, it returns the gateway's URL, and the gateway receives the user id and the success and cancel URLs built from the request's host.
- [x] 3.5 Implement the endpoint and the refusals in `BillingController`. In `StripePaymentGateway`, set `SubscriptionData.Metadata["userId"]`. Verify 3.4 passes

## 4. Keeping the subscription in step with Stripe

- [x] 4.1 Write failing domain and service tests in Chat.Billing.Tests (subscriptions "Stripe's events bring the stored subscription up to date", "The current plan follows the subscription's status"). Replace `SubscriptionServiceTests`' tests of the removed handlers.
  - `Subscription` takes Stripe's state: status, period end, `CancelAtPeriodEnd` and plan. Each Stripe status maps as in design.md's table.
  - An active subscription that ends at the period's end still gives Pro's daily limit.
  - **`SyncSubscriptionAsync`, with a fake gateway and repository:**
    - it creates the record for the metadata user;
    - it updates the user's record;
    - running the same sync twice leaves the same record;
    - a subscription without user metadata, or with an unknown price, is ignored;
    - a cancelled older subscription does not replace an active newer one;
    - a new active subscription replaces a cancelled older one;
    - a `resource_missing` answer from the gateway, surfaced as its own exception, is ignored, while other gateway failures propagate.
- [x] 4.2 Write failing Mongo tests for the subscription repository (design 3):
  - Saving for a user twice leaves one document with the first `_id`.
  - Two saves for a new user started together leave exactly one document.
  - The startup index makes `UserId` unique.
- [x] 4.3 Implement:
  - the domain method;
  - `IPaymentGateway.GetSubscriptionAsync`, returning a snapshot (id, user id, price id, status, period end, cancel at period end), with Stripe's `resource_missing` mapped to its own exception;
  - the sync, the repository's atomic per-user upsert and its unique index at startup;
  - removing the three old handlers.

  Verify 4.1 and 4.2 pass
- [x] 4.4 Write failing webhook API tests, with payloads signed in the test in Stripe's `t=…,v1=…` scheme and a fake gateway:
  - Each event type from design.md triggers a sync of the right subscription id.
  - An invoice-paid event before the checkout event still ends with the user on Pro.
  - A bad or missing signature answers 400 and syncs nothing.
  - An unhandled event type answers 200 and syncs nothing.
  - A missing subscription answers 200.
  - Any other gateway failure answers 500.
- [x] 4.5 Rework `StripeWebhookHandler` and `WebhookController` to match, with `throwOnApiVersionMismatch: false`. Verify 4.4 passes

## 5. Current plan and cancelling

- [x] 5.1 Write failing API tests (subscriptions "The user can see their current plan", "Cancelling keeps Pro until the period ends"):
  - `GET /billing/subscription` answers Free, with a null status and period end, for a user without a subscription.
  - It answers Pro, with the period end and `cancelAtPeriodEnd`, for an active one.
  - It answers Free for a past-due one.
  - `DELETE` asks the gateway to cancel at the period's end, then syncs, so the next GET shows Pro with `cancelAtPeriodEnd: true`.
  - `DELETE` for a Free user answers 404.
- [x] 5.2 Implement the always-answering status from the plan record, and `CancelAtPeriodEndAsync` in the gateway, replacing the immediate cancel. Verify 5.1 passes

## 6. The Manage Plan page

- [x] 6.1 Write failing `billingService` tests:
  - `getCheckoutMode()` returns the mode.
  - `getSubscription()` returns the new shape and never `null`.
  - The existing uncommitted 503 and other-failure tests are kept.
- [x] 6.2 Write failing `BillingPage` tests (checkout "The Manage Plan page tells the user what upgrading means here"; plans "Listed plans show only what they give"; subscriptions "The user can see their current plan"):
  - **Banners:**
    - the test-mode banner shows 4242 4242 4242 4242;
    - in `off` mode, the page says upgrades are turned off and Subscribe is disabled.
  - **Plans:** Free and Pro show as cards reading "10 messages a day" and "100 messages a day", with no document upload or sharing listed.
  - **Current plan:**
    - Free is marked current for a Free user;
    - Pro shows as current with "Renews on <date>", or with "Ends on <date>" and no cancel button.
  - **Failures:**
    - a failed load shows "Manage Plan could not be loaded. Please try again later." with Back to Chat, not a spinner;
    - the existing uncommitted failure-message tests are kept.
- [x] 6.3 Write failing `App` and `BillingPage` tests (checkout "Returning from Stripe opens Manage Plan"), using fake timers:
  - Opening at `/billing/success` shows Manage Plan with the "updates once Stripe confirms" message, and replaces the address with `/`.
  - It re-reads the subscription until it is Pro, at most 10 times.
  - After 10 reads without Pro, it shows "reload the page in a minute".
  - Opening at `/billing/cancel` says nothing was charged.
  - A user not yet signed in sees Manage Plan after signing in.
- [x] 6.4 Implement the service, page and `App` changes. Add the `/billing` proxy to `vite.config.ts` with `changeOrigin: false` and a `bypass` that serves `index.html` to page loads. Verify 6.1 to 6.3 pass, then run `npm run typecheck`, `npm run lint` and `npm run test:run`

## 7. Docs and full verification

- [x] 7.1 Update `docs/deployment.md`:
  - replace `Chat__DailyMessageLimit` with the plan settings, and say to remove it from Render;
  - add the Stripe test-mode setup from design.md's migration plan: the product and USD 19 monthly price, the webhook events, and the three Secret Files.

  Update the README:
  - add a "Try upgrading" section with the test card;
  - fix the "Fair use" bullet, which says 50 a day;
  - fix the Billing feature bullet;
  - update Known limitations;
  - add the configuration notes.

  Verify the docs name no secret value
- [x] 7.2 Add an e2e test: open Manage Plan and see the Free and Pro cards and a mode notice. Run `dotnet test backend/ChatApp.sln`, the frontend suites and `npx playwright test`, and verify all pass
- [x] 7.3 By hand, run the backend with Stripe test values in user secrets, `Billing__Plans__Free__DailyMessages=10` and `Billing__Plans__Pro__DailyMessages=100`, and `stripe listen --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.paid,invoice.payment_succeeded,invoice.payment_failed --forward-to localhost:5064/webhooks/stripe`. Then check:
  - Under the Vite dev server, a new user's 11th message is refused with the Pro hint.
  - Upgrading with 4242 4242 4242 4242 returns to `localhost:5173`, and Manage Plan turns to Pro.
  - The next message is accepted.
  - Cancelling shows "Ends on <date>".
  - Ending the subscription in the Stripe dashboard returns the user to Free.

  Record the results for the pull request
