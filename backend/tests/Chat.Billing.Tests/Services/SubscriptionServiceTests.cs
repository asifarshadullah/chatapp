using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Application.Services;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using FluentAssertions;

namespace Chat.Billing.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

public class FakeSubscriptionRepository : ISubscriptionRepository
{
    private readonly List<Subscription> _store = [];

    public Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(s => s.UserId == userId));

    public Task<Subscription?> GetByStripeIdAsync(string stripeId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(s => s.StripeSubscriptionId == stripeId));

    public Task SaveAsync(Subscription subscription, CancellationToken ct = default)
    {
        _store.RemoveAll(s => s.Id == subscription.Id);
        _store.Add(subscription);
        return Task.CompletedTask;
    }
}

public class FakePaymentGateway : IPaymentGateway
{
    public string? LastCancelledStripeId { get; private set; }
    public CheckoutSessionDto? CheckoutResult { get; set; }

    // Simulate webhook events by providing a list of (stripeSubId, eventType, periodEnd) tuples
    public List<(string stripeSubId, string eventType, DateTime periodEnd)> WebhookEvents { get; } = [];

    public Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl,
        CancellationToken ct = default)
        => Task.FromResult(CheckoutResult ?? new CheckoutSessionDto("https://checkout.stripe.com/pay/cs_test_fake", "cs_test_fake"));

    public Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        LastCancelledStripeId = stripeSubscriptionId;
        return Task.CompletedTask;
    }

    public Task HandleWebhookAsync(string payload, string stripeSignatureHeader, CancellationToken ct = default)
        => Task.CompletedTask; // Not used in unit tests — tested separately
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class SubscriptionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();
    private const string StripePriceId = "price_pro";
    private const string StripeSubId = "sub_test_123";

    private static Subscription MakeActiveSubscription() =>
        new(Guid.NewGuid(), UserId, PlanId, SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(30), StripeSubId);

    // ── Cycle 2.1 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_ReturnsCheckoutUrl()
    {
        var gateway = new FakePaymentGateway
        {
            CheckoutResult = new CheckoutSessionDto("https://checkout.stripe.com/pay/cs_test_abc", "cs_test_abc"),
        };
        var repo = new FakeSubscriptionRepository();
        var sut = new SubscriptionService(gateway, repo);

        var result = await sut.SubscribeAsync(UserId, PlanId, StripePriceId,
            "https://app/success", "https://app/cancel");

        result.CheckoutUrl.Should().Be("https://checkout.stripe.com/pay/cs_test_abc");
        result.SessionId.Should().Be("cs_test_abc");
    }

    // ── Cycle 2.2 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandlePaymentSucceeded_ActivatesSubscription()
    {
        var repo = new FakeSubscriptionRepository();
        var sub = new Subscription(UserId, PlanId, StripeSubId, DateTime.UtcNow.AddDays(30));
        await repo.SaveAsync(sub);

        var gateway = new FakePaymentGateway();
        var sut = new SubscriptionService(gateway, repo);
        var newPeriodEnd = DateTime.UtcNow.AddDays(31);

        await sut.HandlePaymentSucceededAsync(StripeSubId, newPeriodEnd);

        var saved = await repo.GetByStripeIdAsync(StripeSubId);
        saved!.Status.Should().Be(SubscriptionStatus.Active);
        saved.CurrentPeriodEnd.Should().BeCloseTo(newPeriodEnd, TimeSpan.FromSeconds(1));
    }

    // ── Cycle 2.3 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleSubscriptionCancelled_CancelsSubscription()
    {
        var repo = new FakeSubscriptionRepository();
        var sub = MakeActiveSubscription();
        await repo.SaveAsync(sub);

        var sut = new SubscriptionService(new FakePaymentGateway(), repo);

        await sut.HandleSubscriptionCancelledAsync(StripeSubId);

        var saved = await repo.GetByStripeIdAsync(StripeSubId);
        saved!.Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    // ── Cycle 2.4 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandlePaymentSucceeded_SameEventTwice_DoesNotDuplicateOrError()
    {
        var repo = new FakeSubscriptionRepository();
        var sub = new Subscription(UserId, PlanId, StripeSubId, DateTime.UtcNow.AddDays(30));
        await repo.SaveAsync(sub);

        var sut = new SubscriptionService(new FakePaymentGateway(), repo);
        var periodEnd = DateTime.UtcNow.AddDays(31);

        await sut.HandlePaymentSucceededAsync(StripeSubId, periodEnd);
        await sut.HandlePaymentSucceededAsync(StripeSubId, periodEnd); // duplicate

        var saved = await repo.GetByStripeIdAsync(StripeSubId);
        saved!.Status.Should().Be(SubscriptionStatus.Active);
    }

    // ── Cycle 2.5 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleInvoicePaymentFailed_MarksSubscriptionPastDue()
    {
        var repo = new FakeSubscriptionRepository();
        var sub = MakeActiveSubscription();
        await repo.SaveAsync(sub);

        var sut = new SubscriptionService(new FakePaymentGateway(), repo);

        await sut.HandlePaymentFailedAsync(StripeSubId);

        var saved = await repo.GetByStripeIdAsync(StripeSubId);
        saved!.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    // ── Cycle 2.6 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelSubscriptionAsync_CallsGatewayAndUpdatesStatus()
    {
        var repo = new FakeSubscriptionRepository();
        var sub = MakeActiveSubscription();
        await repo.SaveAsync(sub);

        var gateway = new FakePaymentGateway();
        var sut = new SubscriptionService(gateway, repo);

        await sut.CancelSubscriptionAsync(UserId);

        gateway.LastCancelledStripeId.Should().Be(StripeSubId);
        var saved = await repo.GetByUserIdAsync(UserId);
        saved!.Status.Should().Be(SubscriptionStatus.Cancelled);
    }
}
