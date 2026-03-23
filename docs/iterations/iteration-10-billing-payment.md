# Iteration 10: Billing & Payment (Stripe)

## Goal
Users choose a subscription plan (Free, Pro, Enterprise). Stripe handles payment. Subscription
status is persisted in MongoDB and drives the feature gating introduced in Iteration 9.
The stub `StubPlanFeatureService` is replaced with a real implementation backed by the
`Subscription` entity. Application and Domain layers have zero Stripe references.

## Context
Iteration 9 introduced `IPlanFeatureService` with a stub that always returns `true`. This
iteration delivers the real billing bounded context: plan definitions, subscriptions, and the
Stripe integration. The `Billing` bounded context is completely isolated from `Identity` and
`Messaging` — it references users only by their `Guid UserId`.

The critical design: Stripe is the source of truth for payment state. Our database mirrors
that state by consuming Stripe webhook events. We do NOT poll Stripe.

---

## Architecture overview

```
User chooses plan
  └─ POST /billing/subscribe
       └─ SubscriptionService.CreateCheckoutSessionAsync
            └─ IPaymentGateway.CreateCheckoutSessionAsync
                 └─ StripePaymentGateway → Stripe API → returns checkout URL

User completes payment on Stripe-hosted page
  └─ Stripe fires webhook → POST /webhooks/stripe
       └─ StripeWebhookHandler validates Stripe signature
            └─ SubscriptionService.HandleWebhookAsync(event)
                 └─ Subscription entity updated in MongoDB

Next chat request
  └─ IPlanFeatureService.IsFeatureEnabled(userId, Feature.Chat)
       └─ MongoSubscriptionRepository.GetByUserIdAsync(userId)
            └─ returns Plan tier → feature check against plan's feature list
```

---

## New bounded context — folder structure

```
backend/src/
  Chat.Billing.Domain/
    Entities/
      Plan.cs                   Id, Name, Tier, PricePerMonth, Features (List<Feature>)
      Subscription.cs           Id, UserId, PlanId, Status, CurrentPeriodEnd, StripeSubscriptionId
    Enums/
      PlanTier.cs               Free, Pro, Enterprise
      SubscriptionStatus.cs     Active, Cancelled, PastDue, Trialing

  Chat.Billing.Application/
    Interfaces/
      IPlanFeatureService.cs    IsFeatureEnabled(UserId, Feature) → bool  (defined in Iter 9)
      ISubscriptionRepository.cs
      IPaymentGateway.cs        CreateCheckoutSessionAsync, CancelAsync, HandleWebhookAsync
    Services/
      SubscriptionService.cs    Business logic: subscribe, cancel, handle events
    DTOs/
      CheckoutSessionDto.cs     CheckoutUrl, SessionId
      SubscriptionStatusDto.cs  PlanName, Tier, Status, CurrentPeriodEnd

  Chat.Billing.Infrastructure/
    Payment/
      StripePaymentGateway.cs   Implements IPaymentGateway via Stripe .NET SDK
      StripeWebhookHandler.cs   Validates signature, parses event, calls SubscriptionService
    Repositories/
      MongoSubscriptionRepository.cs
      MongoPlanRepository.cs
    Data/
      SubscriptionDocument.cs, PlanDocument.cs
    Configuration/
      StripeSettings.cs         SecretKey, WebhookSecret, PriceIds (per plan tier)
    Services/
      RealPlanFeatureService.cs Replaces StubPlanFeatureService from Iteration 9

tests/
  Chat.Billing.Tests/
    Services/SubscriptionServiceTests.cs    FakePaymentGateway + FakeSubscriptionRepo
    Integration/BillingEndpointTests.cs
    Webhooks/StripeWebhookHandlerTests.cs
```

---

## Phase 1: Domain — Plan and Subscription entities

### Task 1.1: PlanTier and SubscriptionStatus enums
```csharp
public enum PlanTier { Free, Pro, Enterprise }
public enum SubscriptionStatus { Active, Cancelled, PastDue, Trialing }
```

### Task 1.2: Plan entity
```csharp
public class Plan
{
    public Guid Id { get; }
    public string Name { get; }
    public PlanTier Tier { get; }
    public decimal PricePerMonth { get; }
    public IReadOnlyList<Feature> Features { get; }

    public Plan(Guid id, string name, PlanTier tier, decimal pricePerMonth, IEnumerable<Feature> features)
    {
        Id = id; Name = name; Tier = tier; PricePerMonth = pricePerMonth;
        Features = features.ToList().AsReadOnly();
    }

    public bool Includes(Feature feature) => Features.Contains(feature);
}
```

### Task 1.3: Subscription entity (with business rules)
```csharp
public class Subscription
{
    public Guid Id { get; }
    public Guid UserId { get; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime CurrentPeriodEnd { get; private set; }
    public string StripeSubscriptionId { get; }

    // Create new
    public Subscription(Guid userId, Guid planId, string stripeSubscriptionId, DateTime periodEnd)
    {
        Id = Guid.NewGuid();
        UserId = userId; PlanId = planId;
        StripeSubscriptionId = stripeSubscriptionId;
        Status = SubscriptionStatus.Trialing;
        CurrentPeriodEnd = periodEnd;
    }

    // Reconstruct from storage
    public Subscription(Guid id, Guid userId, Guid planId, SubscriptionStatus status,
        DateTime periodEnd, string stripeSubscriptionId)
    { /* assign all fields */ }

    // Domain methods — enforce invariants
    public void Activate(DateTime newPeriodEnd)
    {
        if (Status == SubscriptionStatus.Cancelled)
            throw new InvalidOperationException("Cannot activate a cancelled subscription.");
        Status = SubscriptionStatus.Active;
        CurrentPeriodEnd = newPeriodEnd;
    }

    public void Cancel() => Status = SubscriptionStatus.Cancelled;
    public void MarkPastDue() => Status = SubscriptionStatus.PastDue;
    public void ChangePlan(Guid newPlanId) => PlanId = newPlanId;
}
```

**Key invariant:** A cancelled subscription cannot be reactivated via `Activate()` — a new
subscription must be created. This prevents billing edge cases where Stripe could theoretically
send an activation event for a subscription the user explicitly cancelled.

---

## Phase 2: Application interfaces

### Task 2.1: ISubscriptionRepository
```csharp
public interface ISubscriptionRepository
{
    Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Subscription?> GetByStripeIdAsync(string stripeSubscriptionId, CancellationToken ct = default);
    Task SaveAsync(Subscription subscription, CancellationToken ct = default);
}
```

### Task 2.2: IPaymentGateway
```csharp
public interface IPaymentGateway
{
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl,
        CancellationToken ct = default);

    Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default);

    Task HandleWebhookAsync(string payload, string stripeSignatureHeader, CancellationToken ct = default);
}
```

### Task 2.3: SubscriptionService (TDD)
**File:** `backend/tests/Chat.Billing.Tests/Services/SubscriptionServiceTests.cs`

Use `FakePaymentGateway` and `FakeSubscriptionRepository`:

- **Cycle 2.1:** RED `SubscribeAsync_ReturnsCheckoutUrl`
  → GREEN: call IPaymentGateway.CreateCheckoutSessionAsync → return CheckoutSessionDto

- **Cycle 2.2:** RED `HandleWebhookAsync_PaymentSucceeded_ActivatesSubscription`
  → GREEN: find subscription by stripeId → call subscription.Activate(newPeriodEnd) → save

- **Cycle 2.3:** RED `HandleWebhookAsync_SubscriptionCancelled_CancelsSubscription`
  → GREEN: find → call subscription.Cancel() → save

- **Cycle 2.4:** RED `HandleWebhookAsync_SameEventTwice_DoesNotDuplicateOrError`
  → GREEN: idempotent — if status already matches, no error (Stripe can send duplicates)

- **Cycle 2.5:** RED `HandleWebhookAsync_InvoicePaymentFailed_MarksSubscriptionPastDue`
  → GREEN: find → call subscription.MarkPastDue() → save

- **Cycle 2.6:** RED `CancelSubscriptionAsync_CallsGatewayAndUpdatesStatus`
  → GREEN: call IPaymentGateway.CancelSubscriptionAsync + subscription.Cancel() + save

---

## Phase 3: RealPlanFeatureService (replaces stub)

```csharp
public class RealPlanFeatureService : IPlanFeatureService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPlanRepository _plans;

    public async Task<bool> IsFeatureEnabled(Guid userId, Feature feature, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByUserIdAsync(userId, ct);

        // No subscription or not active/trialing → Free tier only
        if (subscription is null || subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.PastDue)
            return GetFreeTierFeatures().Contains(feature);

        var plan = await _plans.GetByIdAsync(subscription.PlanId, ct);
        return plan?.Includes(feature) ?? false;
    }

    private static HashSet<Feature> GetFreeTierFeatures() =>
        [Feature.Chat]; // Free users can only chat, no uploads/sharing/custom models
}
```

**TDD cycles:**
- **Cycle 3.1:** RED `IsFeatureEnabled_ActiveProSubscription_Chat_ReturnsTrue`
- **Cycle 3.2:** RED `IsFeatureEnabled_ActiveProSubscription_DocumentUpload_ReturnsTrue`
- **Cycle 3.3:** RED `IsFeatureEnabled_NoSubscription_Chat_ReturnsTrue` (Free tier fallback)
- **Cycle 3.4:** RED `IsFeatureEnabled_NoSubscription_DocumentUpload_ReturnsFalse`
- **Cycle 3.5:** RED `IsFeatureEnabled_CancelledSubscription_DocumentUpload_ReturnsFalse`

---

## Phase 4: Infrastructure — Stripe

### Task 4.1: StripeSettings
```csharp
public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public Dictionary<string, string> PriceIds { get; set; } = new();
    // e.g. { "Pro": "price_xxx", "Enterprise": "price_yyy" }
}
```

### Task 4.2: StripePaymentGateway
```csharp
public class StripePaymentGateway : IPaymentGateway
{
    // Uses Stripe.net SDK
    // CreateCheckoutSessionAsync → SessionService.CreateAsync()
    // CancelSubscriptionAsync → SubscriptionService.CancelAsync()
    // HandleWebhookAsync → EventUtility.ConstructEvent() validates signature
    //   then routes by event.Type to SubscriptionService
}
```

### Task 4.3: Webhook endpoint (no [Authorize] — Stripe signature validates)
```csharp
[HttpPost("/webhooks/stripe")]
[AllowAnonymous]
public async Task<IActionResult> StripeWebhook()
{
    var payload = await new StreamReader(Request.Body).ReadToEndAsync();
    var signature = Request.Headers["Stripe-Signature"];
    await _subscriptionService.HandleWebhookAsync(payload, signature, HttpContext.RequestAborted);
    return Ok();
}
```

### Task 4.4: New billing endpoints
```
GET  /billing/plans                      → list all plans with prices and features
POST /billing/subscribe                  body: { planId } → CheckoutSessionDto (redirectUrl)
DELETE /billing/subscription             cancel current subscription
GET  /billing/subscription               current subscription status → SubscriptionStatusDto
POST /webhooks/stripe                    Stripe webhook (AllowAnonymous + signature validation)
```

---

## Phase 5: Wire up in Program.cs

```csharp
// Billing bounded context
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddScoped<ISubscriptionRepository, MongoSubscriptionRepository>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Replace stub from Iteration 9 with real implementation
builder.Services.AddScoped<IPlanFeatureService, RealPlanFeatureService>();
```

---

## appsettings additions
```json
"Stripe": {
  "SecretKey": "sk_test_...",
  "WebhookSecret": "whsec_...",
  "PriceIds": {
    "Pro": "price_xxx",
    "Enterprise": "price_yyy"
  }
}
```

Use Stripe test mode keys during development. Use Stripe CLI to forward webhooks locally:
```bash
stripe listen --forward-to localhost:5064/webhooks/stripe
```

---

## Acceptance criteria
1. GET /billing/plans returns Free, Pro, Enterprise with correct prices
2. POST /billing/subscribe returns a Stripe Checkout URL
3. Complete payment on Stripe test mode → webhook fires → Subscription.Status = Active in MongoDB
4. Active Pro subscription → Feature.DocumentUpload enabled
5. Cancel subscription → status Cancelled → Feature.DocumentUpload disabled
6. Same webhook event fired twice → no duplicate, no error (idempotent handler)
7. PastDue subscription → Feature.Chat still enabled (Free tier fallback), Pro features disabled
8. All tests pass with FakePaymentGateway — no real Stripe calls in automated tests

## Verification commands
```bash
dotnet test backend/ChatApp.sln --verbosity normal
stripe listen --forward-to localhost:5064/webhooks/stripe  # Stripe CLI
cd backend/src/Chat.Api && dotnet run
# Manually trigger checkout, complete with Stripe test card 4242 4242 4242 4242
```

## What you will learn
- New bounded context in practice — Billing has no knowledge of Messaging or Identity internals
- Domain invariants on Subscription: cancelled cannot be reactivated (business rule in entity)
- The Webhook pattern — event-driven state sync; polling is the wrong approach here
- Idempotency in webhook handlers — Stripe can fire the same event multiple times
- Replacing a stub with a real implementation — StubPlanFeatureService → RealPlanFeatureService
- Lean JWT validated: plan tier checked live from DB, never from the token
- The IPaymentGateway abstraction: Stripe can be replaced without touching Application or Domain

## Decisions log
| Decision | Reason |
|---|---|
| Separate Chat.Billing.Domain project | Billing context evolves independently; no shared entities with Messaging or Identity |
| Stripe as Infrastructure behind IPaymentGateway | Swap Stripe → Paddle/Braintree requires one new class, one DI line change |
| Webhook as source of truth | Stripe is authoritative for payment state; polling is slow and unreliable |
| Idempotent webhook handler | Stripe guarantees at-least-once delivery — duplicates must be safe |
| Cancelled subscription cannot be reactivated via Activate() | Domain invariant: prevents accidental reactivation from duplicate Stripe events |
| Free tier fallback when no subscription | Graceful default: unsubscribed users get basic chat; no null exceptions |
| RealPlanFeatureService replaces stub in same DI registration | One-line swap in Program.cs; no other code changes needed |
