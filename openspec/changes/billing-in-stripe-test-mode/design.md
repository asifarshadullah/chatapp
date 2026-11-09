## Context

**Billing today.**
- `BillingController`:
  - `GET /billing/plans` lists whatever is in the `plans` collection, and nothing ever writes to it.
  - `POST /billing/subscribe` looks up the plan, maps its tier to `Stripe:PriceIds[tier]` and asks `IPaymentGateway` for a checkout session. The session is created with `ClientReferenceId = userId`.
  - `GET /billing/subscription` answers 404 without a subscription, and otherwise returns the plan's id as its name.
  - `DELETE /billing/subscription` cancels at Stripe immediately.
- `StripeWebhookHandler` verifies the signature and handles `invoice.payment_succeeded`, `invoice.payment_failed` and `customer.subscription.deleted`, applying each payload to a subscription found by its Stripe id. Nothing creates a subscription, so all three find nothing.
- `RealPlanFeatureService` gives a user without an active or trialing subscription only `Chat`; with one, the plan's feature list.
- `StripePaymentGateway` sets the static `StripeConfiguration.ApiKey` in its constructor.
- Stripe.net is pinned to `45.*`, whose API version still has `Subscription.CurrentPeriodEnd` and `Invoice.SubscriptionId`.
- `PlanDocument` and `SubscriptionDocument` do not ignore extra elements, so a field added now breaks an older image that reads it.
- **Ids:** their `Guid` fields have no representation set, so MongoDB driver 2.30 stores them as legacy binary. Chat and Identity store `Guid`s as strings. Both billing collections are empty on the live demo.
- **No Mongo tests:** nothing tests the billing repositories against MongoDB. `Chat.Billing.Tests` has no Mongo fixture.
- **Billing page:** `BillingPage` loads with a `Promise.all` and no `catch`, so one failed request leaves it spinning forever. That is what Manage Plan does under the Vite dev server today, which does not proxy `/billing`.
- **Dev proxy:** the `/api` and `/auth` proxies use `changeOrigin: true`, so the backend sees `localhost:5064` as the host. A checkout's return URLs are built from the request's host.

**Tests today.**
- `ChatApiFactory` replaces the quota with `InMemoryDailyMessageQuota`, whose `Limit` tests set, and keeps the real `IPlanFeatureService`.
- `AuthApiFactory` uses `UnlimitedDailyMessageQuota` and `AlwaysEnabledFeatureService`.
- `PlanFeatureFactories` has two more `IPlanFeatureService` fakes.
- `ModelWiringTests` and `ModelOptionsStartupTests` use `Chat:DailyMessageLimit`.
- `SubscriptionServiceTests` covers the three handlers this change removes.

**The daily limit today.**
- `ChatSettings.DailyMessageLimit` (50; 1000 in Development) is passed into `MongoDailyMessageQuota`'s constructor.
- `ChatService` calls `TryConsumeAsync(userId)` after the conversation check and throws `DailyMessageLimitReachedException(_quota.Limit)`.
- `ChatController` turns that into a 429.
- The contexts share only `Guid UserId`. Only `Chat.Api` references all three.

## Goals / Non-Goals

**Goals:**
- The stored subscription is correct whatever order Stripe's events arrive in, and however often each arrives.
- Chat never references Billing: the plan's limit reaches the chat through a port the composition root fills.
- Nothing in CI talks to Stripe. Webhooks are tested with payloads signed in the test.
- An image built before this change keeps working if it is rolled back to, after this one has written to the database.

**Non-Goals:**
- More than one paid plan, or switching between paid plans.
- Keeping a local copy of Stripe customers or invoices.

## Decisions

### 1. Every event re-reads the subscription from Stripe

The webhook handler takes the Stripe subscription id from the event and calls one service method, `SyncSubscriptionAsync(stripeSubscriptionId)`:
- **Which events:** `checkout.session.completed` (its `SubscriptionId`), `customer.subscription.created`, `.updated` and `.deleted`, and `invoice.payment_succeeded`, `invoice.paid` and `invoice.payment_failed` (their `SubscriptionId`).
- **What it does:** the method reads the subscription through `IPaymentGateway.GetSubscriptionAsync` and stores what Stripe says now.

Stripe does not promise delivery order, and it retries. Applying each event's payload would need timestamps compared per field, and a payment-succeeded event that arrives before the checkout event would still find nothing to update. Reading the current state makes every event mean "look again", so order and repeats do not matter.

- **Cost:** one Stripe API call per event, which is a handful per upgrade.
- **Alternative rejected:** handling only `customer.subscription.*` from their payloads. It is still order-sensitive, and it does not use the checkout event that ties the user.

### 2. The user travels on the subscription's metadata

Checkout sets `SubscriptionData.Metadata["userId"]`, as well as the existing `ClientReferenceId`. The subscription read in decision 1 then names its user, whichever event led to it.

- **Without a user:** a subscription with no `userId` metadata, for example one created by hand in the dashboard, is logged and ignored.
- **The plan:** it comes from the subscription's price. The gateway maps the price to a tier through `Stripe:PriceIds`, so the snapshot it returns carries the tier, and Application never sees a Stripe price ID. Any other price is logged and ignored.
- **The outcome:** the sync returns a `SyncResult` (`Saved`, `NotTiedToUser`, `UnknownPlan`, `MissingAtGateway`, `Superseded`). The webhook handler logs every outcome except `Saved`.

### 3. One record per user, and the latest subscription decides

`subscriptions` keeps one document per user, enforced by a unique index on `UserId` created at startup. A sync reads the user's record, decides, and writes with one `UpdateOne` filtered on `UserId`, with `IsUpsert` and the record's `_id` set only on insert:
- **Simultaneous events:** two syncs of a brand-new subscription arriving together cannot create two records. Duplicate-key retries on a unique-index upsert are handled by the server.
- **The latest subscription decides:** when the user's record holds a different Stripe subscription, the incoming one replaces it only if the incoming one grants Pro, or the stored one does not. A late `deleted` event for a user's old subscription therefore cannot take Pro away from their new one, and a user who comes back after cancelling gets their record replaced by the new subscription.
- **Accepted race:** two syncs of *different* subscriptions for one user at the same moment could still apply in the wrong order. That needs two live subscriptions at once, which the 409 in decision 7 prevents.

### 4. Stripe's statuses map to four

| Stripe status | Stored as | Grants Pro |
|---|---|---|
| `active` | Active | yes |
| `trialing` | Trialing | yes |
| `past_due`, `unpaid`, `incomplete`, `paused` | PastDue | no |
| `canceled`, `incomplete_expired` | Cancelled | no |

A new `CancelAtPeriodEnd` flag is stored alongside the status. `Subscription` gets one method that takes Stripe's state, and keeps the guard that a cancelled subscription is not brought back except by a newer subscription id. The old per-event methods go once nothing calls them.

### 4b. Billing documents store ids as strings

`PlanDocument` and `SubscriptionDocument` get `[BsonRepresentation(BsonType.String)]` on every `Guid`, as Chat and Identity have:
- **Why:** it makes the format the same across contexts, and stops the billing data depending on driver 2.x's legacy default, which driver 3 drops.
- **No migration:** both collections are empty on the live demo.
- **Locally:** a developer's old billing documents are removed by the seeder (plans) or simply not found (subscriptions).

### 5. Plans live in configuration and are upserted by tier

- **Configuration:** `Billing:Plans:Free` and `Billing:Plans:Pro` each hold `Name`, `PricePerMonth` and `DailyMessages`. They are validated at startup: the limit must be positive, and the price must not be negative.
- **Seeding:** `PlanSeeder.SeedAsync` runs in the startup scope next to `RoleSeeder`:
  - for each tier, it updates the existing record or inserts one with a new id;
  - it deletes records of any other tier;
  - it deletes plan records whose `_id` is not a string, written before decision 4b, which can no longer be read;
  - it adds a unique index on `Tier`.
- **Why configuration and not a fixed seed:** Development needs both limits at 1000 so that one end-to-end user can run the whole suite, and production may need to lower Free if Groq's allowance runs out. Both are configuration changes, not code.
- **What a plan record holds:** the limit is stored, so the plan list and the chat read the same number. `Features` stays as `[Chat]` for both plans and is no longer shown.
- **Alternative rejected:** fixed ids per tier. Keeping the existing id is enough and needs no magic constants.

### 6. The limit crosses into Chat through a port

- **Billing's side:** `IPlanFeatureService` gains `GetMessageAllowanceAsync(userId) → MessageAllowance(int Limit, int? UpgradeLimit)`:
  - **Limit:** the limit of the plan the user is on;
  - **UpgradeLimit:** Pro's limit when that plan is Free, and null on Pro.
  - It reads plans through a new `IPlanRepository.GetByTierAsync`.
- **The port:** Chat.Application gains `IDailyMessageAllowance.GetAsync(userId) → DailyAllowance(int Limit, int? UpgradeLimit)`.
- **The quota:** `IDailyMessageQuota.TryConsumeAsync(userId, limit)` takes the limit per call, and `ChatSettings` is removed with its only setting. The counter document is unchanged, so a user who upgrades mid-day is measured against the new limit with the day's count intact.
- **The service:** `ChatService` asks for the allowance where it consumes the quota today, after the conversation check, so a refused message still counts nothing. It throws `DailyMessageLimitReachedException(limit, upgradeLimit)`.
- **The adapter:** `Chat.Api` holds `PlanDailyMessageAllowance`, which maps Billing's `MessageAllowance` to Chat's `DailyAllowance`.
- **The refusal:** the controller builds the 429 message, adding the Pro sentence when `UpgradeLimit` is set.
- **Alternative rejected:** Chat.Application referencing Billing.Application. That breaks the boundary the README names as a design decision.

### 7. Checkout availability is one rule in `StripeSettings`

- **Mode:** `Mode` is `off` unless `SecretKey`, `WebhookSecret` and `PriceIds:Pro` are all non-empty and contain no `placeholder`. Otherwise it is `live` for an `sk_live_` or `rk_live_` key, and `test` for anything else.
- **The live-key guard:** a `ValidateOnStart` check refuses `live` unless `Stripe:AllowLiveKey` is true, and the error message names that setting.
- **The mode endpoint:** `GET /billing/checkout-mode` returns `{ "mode": "test" | "live" | "off" }` to any signed-in user.
- **Subscribe refusals:** `POST /billing/subscribe` checks `Mode` first and answers 503 without touching the plan store or Stripe. It then answers 404 for an unknown plan, 400 for Free and 409 when the user already has Pro.
- **Stripe failures:**
  - The gateway wraps every `StripeException` in Billing's own `PaymentGatewayException`, so the API stays free of Stripe types. Subscribe logs it and answers 502.
  - `resource_missing` becomes `SubscriptionNotFoundAtGatewayException` instead (decision 8).
  - Cancelling answers 502 with "The subscription could not be cancelled. Please try again later." when Stripe refuses.
- **Enterprise:** `PriceIds:Enterprise` leaves the shipped settings.

### 8. Webhook parsing tolerates the API version

`EventUtility.ConstructEvent` is called with `throwOnApiVersionMismatch: false`.
- **Why it's safe:** the handler reads only ids from the event, and the subscription itself is read through the SDK at its pinned version. A dashboard webhook created with the account's newer default version therefore still works.
- **Responses:**
  - **A signature failure** gives 400.
  - **A subscription Stripe no longer knows:** if reading it returns `resource_missing`, the event is logged and answered 200, since retrying cannot help.
  - **Any other failure to reach Stripe** answers 500, so Stripe retries later.

### 9. Cancelling sets `cancel_at_period_end`

- **The gateway:** `IPaymentGateway.CancelAtPeriodEndAsync` replaces the immediate cancel.
- **After cancelling:** the endpoint runs the same sync, so the page shows "Ends on <date>" at once, without waiting for the webhook.
- **When the period ends:** Stripe sends `customer.subscription.deleted`, and the sync stores Cancelled.

### 10. The current subscription is always answered

`GET /billing/subscription` returns `{ planName, tier, status, currentPeriodEnd, cancelAtPeriodEnd }`:
- **On Free:** `planName: "Free"`, a null period end, and `cancelAtPeriodEnd: false`.
- **The name:** it comes from the plan record, which fixes the id-as-name bug.

### 11. The frontend

- **Loading the page:** `BillingPage` loads plans, the current subscription and the checkout mode together. Each plan is described by `"<n> messages a day"`.
- **Messages:** the notices and the checkout error share one area above the cards. Notices use `role="status"`, and only a failed checkout start is an `alert`.
- **Cards:** each plan is a `section` named after the plan, marked "Current plan" when it is.
- **Returning from Stripe:** `App` checks `location.pathname` on mount. For `/billing/success` or `/billing/cancel` it opens the billing view with a `returnedFrom` prop and `replaceState`s to `/`.
- **Waiting for the upgrade:** after `success`, the page re-reads the subscription every 3 seconds, for up to 10 tries, and stops once it is Pro. The timer stops itself, so no check runs after the last one while React re-renders.
- **Loading the page:** a failure in any of the three requests shows "Manage Plan could not be loaded. Please try again later." with the Back button, replacing today's endless spinner.
- **Signing in first:** the `returnedFrom` state is set from the address when `App` mounts, before the user is known, so it survives a sign-in that happens first.
- **Local development:**
  - **The proxy:** Vite proxies `/billing` with `changeOrigin: false`, so the backend sees `localhost:5173` and builds Stripe's return URLs for the dev server.
  - **Page loads:** its `bypass` serves `index.html` for page loads (requests that accept `text/html`), so `/billing/success` opens the app rather than reaching the API.
  - **Webhooks:** `/webhooks` is not proxied, because `stripe listen` forwards straight to the backend.

### 11b. Test doubles follow the new seams

- **`ChatApiFactory`:** it replaces `IDailyMessageAllowance` with a settable `FakeDailyMessageAllowance` (unlimited by default). `InMemoryDailyMessageQuota` only counts against the limit it is given. Tests that set `factory.Quota.Limit` set `factory.Allowance.Limit` instead.
- **`AuthApiFactory` and `PlanFeatureFactories`:** their `IPlanFeatureService` fakes implement `GetMessageAllowanceAsync` with an unlimited allowance.
- **Mongo tests:** `Chat.Billing.Tests` gets a Mongo fixture like `Chat.Infrastructure.Tests`' (port 27018, database dropped after the run), for the seeder and the repositories.
- **Webhook tests:** they sign payloads with the same HMAC-SHA256 `t=…,v1=…` scheme Stripe uses, with a test secret set through `UseSetting`, and replace `IPaymentGateway` with a fake.

### 12. Old documents stay readable both ways

`PlanDocument` and `SubscriptionDocument` gain `[BsonIgnoreExtraElements]`:
- **Rolling back:** this change ships first in the same image that writes the new fields. A later rollback to that image or newer is safe. A rollback to an image from before this change can still fail on the new fields, as noted in the risks.
- **Moving forward:** new fields get defaults when missing, so older subscription documents read as not cancelling.

## Risks / Trade-offs

- **[Render's cold start delays the first webhook]** → Stripe retries failed deliveries for days. The return page tells the user the plan updates once Stripe confirms, and checks for 30 seconds. After that, a reload shows it.
- **[Anyone can get Pro with the test card]** → This is accepted for a demo. Groq's shared daily allowance of about 100 messages still bounds total use, and Pro's 100 a day is no more than that.
- **[A rollback to an image from before this change reads new fields or string ids]** → The live database has no real subscriptions today, and the new plan records are the only ones written. If a rollback is needed, delete the `plans` and `subscriptions` documents first, or roll forward instead.
- **[Concurrent syncs of two different subscriptions for one user]** → The 409 on Subscribe prevents a second live subscription, so this needs a subscription created outside the app. It is accepted.
- **[Stripe's API version moves on]** → Stripe.net is pinned to `45.*` (installed 45.14.0), whose models still carry `Subscription.CurrentPeriodEnd` and `Invoice.SubscriptionId`. Upgrading past the version that moves them is a separate change.
- **[The plan price shown and Stripe's price can differ]** → The price on the card comes from configuration and the charge from Stripe's price object. `docs/deployment.md` says to create the Stripe price at the configured amount.
- **[`StripeConfiguration.ApiKey` is process-wide]** → There is one key per process, so this is acceptable. The gateway keeps setting it.
- **[The Free limit of 10 is tight for the end-to-end suite]** → Development sets both plans to 1000. A test that exercises the limit sets its own plan limit.

## Migration Plan

1. **Merge and deploy:** startup writes Free and Pro into Atlas.
   - Until the secrets are added, the page shows "Upgrades are turned off", and the limit is Free's 10 for everyone.
   - `Chat__DailyMessageLimit` on Render, if set, is no longer read: remove it, and set `Billing__Plans__Free__DailyMessages` instead if Free needs to be lower.
2. **In the Stripe dashboard, in test mode:**
   - create a Pro product with a monthly price of 19;
   - add a webhook endpoint for `https://chatapp-2uzj.onrender.com/webhooks/stripe` with the events from decision 1;
   - copy the signing secret.
3. **Add the Render Secret Files** `Stripe__SecretKey`, `Stripe__WebhookSecret` and `Stripe__PriceIds__Pro`, then redeploy.
4. **Check on the live site:**
   - the banner shows test mode;
   - a new user upgrades with 4242 4242 4242 4242 and becomes Pro;
   - cancelling shows "Ends on".
5. **Rollback:** redeploy the previous image after deleting the `plans` and `subscriptions` documents, or remove the Stripe secrets to turn checkout off without a rollback.
