using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Application.Services;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using FluentAssertions;

namespace Chat.Billing.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

/// <summary>One subscription per user; saving again keeps the record's first id, as MongoDB does.</summary>
public class FakeSubscriptionRepository : ISubscriptionRepository
{
    private readonly List<Subscription> _store = [];

    public int Count => _store.Count;

    public Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(s => s.UserId == userId));

    public Task SaveAsync(Subscription subscription, CancellationToken ct = default)
    {
        var index = _store.FindIndex(s => s.UserId == subscription.UserId);
        if (index < 0)
            _store.Add(subscription);
        else
            _store[index] = new Subscription(_store[index].Id, subscription.UserId, subscription.PlanId, subscription.Status,
                subscription.CurrentPeriodEnd, subscription.StripeSubscriptionId, subscription.CancelAtPeriodEnd);
        return Task.CompletedTask;
    }
}

public class FakePaymentGateway : IPaymentGateway
{
    private readonly Dictionary<string, StripeSubscriptionSnapshot> _subscriptions = [];

    public string? LastCancelledStripeId { get; private set; }
    public CheckoutSessionDto? CheckoutResult { get; set; }
    public Exception? ReadFailure { get; set; }

    public void Holds(StripeSubscriptionSnapshot snapshot) => _subscriptions[snapshot.Id] = snapshot;

    public Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl,
        CancellationToken ct = default)
        => Task.FromResult(CheckoutResult ?? new CheckoutSessionDto("https://checkout.stripe.com/pay/cs_test_fake", "cs_test_fake"));

    public Task CancelAtPeriodEndAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        LastCancelledStripeId = stripeSubscriptionId;
        if (_subscriptions.TryGetValue(stripeSubscriptionId, out var snapshot))
            _subscriptions[stripeSubscriptionId] = snapshot with { CancelAtPeriodEnd = true };
        return Task.CompletedTask;
    }

    public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        if (ReadFailure is not null)
            throw ReadFailure;
        return _subscriptions.TryGetValue(stripeSubscriptionId, out var snapshot)
            ? Task.FromResult(snapshot)
            : throw new SubscriptionNotFoundAtGatewayException(stripeSubscriptionId);
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class SubscriptionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Plan Free = new(Guid.NewGuid(), "Free", PlanTier.Free, 0m, 10, [Feature.Chat]);
    private static readonly Plan Pro = new(Guid.NewGuid(), "Pro", PlanTier.Pro, 19m, 100, [Feature.Chat]);
    private static readonly DateTime PeriodEnd = new(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeSubscriptionRepository _repo = new();
    private readonly FakePaymentGateway _gateway = new();
    private readonly SubscriptionService _sut;

    public SubscriptionServiceTests()
    {
        _sut = new SubscriptionService(_gateway, _repo, new FakePlanRepository(Free, Pro));
    }

    private static StripeSubscriptionSnapshot AtStripe(string id, SubscriptionStatus status, bool cancelAtPeriodEnd = false,
        Guid? userId = null, PlanTier? tier = PlanTier.Pro) =>
        new(id, userId ?? UserId, tier, status, PeriodEnd, cancelAtPeriodEnd);

    private Task<Subscription?> Stored() => _repo.GetByUserIdAsync(UserId);

    // ── Starting a checkout ──────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_ReturnsCheckoutUrl()
    {
        _gateway.CheckoutResult = new CheckoutSessionDto("https://checkout.stripe.com/pay/cs_test_abc", "cs_test_abc");

        var result = await _sut.SubscribeAsync(UserId, Pro.Id, "price_pro", "https://app/success", "https://app/cancel");

        result.CheckoutUrl.Should().Be("https://checkout.stripe.com/pay/cs_test_abc");
        result.SessionId.Should().Be("cs_test_abc");
    }

    [Fact]
    public async Task SubscribeAsync_ForAUserAlreadyOnPro_Throws()
    {
        await _repo.SaveAsync(new Subscription(Guid.NewGuid(), UserId, Pro.Id, SubscriptionStatus.Active, PeriodEnd, "sub_1"));

        var act = () => _sut.SubscribeAsync(UserId, Pro.Id, "price_pro", "https://app/success", "https://app/cancel");

        await act.Should().ThrowAsync<AlreadySubscribedException>();
    }

    // ── Keeping the subscription in step with Stripe ─────────────────────────

    [Fact]
    public async Task Sync_ANewSubscription_IsStoredForTheUserItNames()
    {
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active));

        var result = await _sut.SyncSubscriptionAsync("sub_1");

        result.Should().Be(SyncResult.Saved);
        var stored = await Stored();
        stored.Should().BeEquivalentTo(new
        {
            UserId, PlanId = Pro.Id, Status = SubscriptionStatus.Active, CurrentPeriodEnd = PeriodEnd,
            StripeSubscriptionId = "sub_1", CancelAtPeriodEnd = false,
        });
    }

    [Fact]
    public async Task Sync_AChangedSubscription_UpdatesTheUsersRecord()
    {
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active));
        await _sut.SyncSubscriptionAsync("sub_1");
        var id = (await Stored())!.Id;
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active, cancelAtPeriodEnd: true));

        await _sut.SyncSubscriptionAsync("sub_1");

        var stored = await Stored();
        stored!.Id.Should().Be(id);
        stored.CancelAtPeriodEnd.Should().BeTrue();
        _repo.Count.Should().Be(1);
    }

    [Fact]
    public async Task Sync_TheSameStateTwice_LeavesTheSameRecord()
    {
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active));
        await _sut.SyncSubscriptionAsync("sub_1");
        var first = await Stored();

        await _sut.SyncSubscriptionAsync("sub_1");

        (await Stored()).Should().BeEquivalentTo(first);
        _repo.Count.Should().Be(1);
    }

    [Fact]
    public async Task Sync_ASubscriptionNotTiedToAUser_IsIgnored()
    {
        _gateway.Holds(new StripeSubscriptionSnapshot("sub_1", null, PlanTier.Pro, SubscriptionStatus.Active, PeriodEnd, false));

        (await _sut.SyncSubscriptionAsync("sub_1")).Should().Be(SyncResult.NotTiedToUser);
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Sync_ASubscriptionForAnUnknownPrice_IsIgnored()
    {
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active, tier: null));

        (await _sut.SyncSubscriptionAsync("sub_1")).Should().Be(SyncResult.UnknownPlan);
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Sync_ALateEventAboutAnOldSubscription_DoesNotTakeProAway()
    {
        _gateway.Holds(AtStripe("sub_new", SubscriptionStatus.Active));
        await _sut.SyncSubscriptionAsync("sub_new");
        _gateway.Holds(AtStripe("sub_old", SubscriptionStatus.Cancelled));

        var result = await _sut.SyncSubscriptionAsync("sub_old");

        result.Should().Be(SyncResult.Superseded);
        var stored = await Stored();
        stored!.StripeSubscriptionId.Should().Be("sub_new");
        stored.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Sync_ANewSubscriptionAfterACancelledOne_ReplacesIt_KeepingTheRecord()
    {
        _gateway.Holds(AtStripe("sub_old", SubscriptionStatus.Cancelled));
        await _sut.SyncSubscriptionAsync("sub_old");
        var id = (await Stored())!.Id;
        _gateway.Holds(AtStripe("sub_new", SubscriptionStatus.Active));

        await _sut.SyncSubscriptionAsync("sub_new");

        var stored = await Stored();
        stored!.Id.Should().Be(id);
        stored.StripeSubscriptionId.Should().Be("sub_new");
        stored.GrantsPlan.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_ASubscriptionStripeNoLongerKnows_IsIgnored()
    {
        (await _sut.SyncSubscriptionAsync("sub_gone")).Should().Be(SyncResult.MissingAtGateway);
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Sync_WhenStripeCannotBeReached_Throws_SoStripeTriesAgain()
    {
        _gateway.ReadFailure = new PaymentGatewayException("timeout", new TimeoutException());

        var act = () => _sut.SyncSubscriptionAsync("sub_1");

        await act.Should().ThrowAsync<PaymentGatewayException>();
    }

    // ── Cancelling ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelling_AsksStripeToEndAtThePeriodsEnd_AndKeepsPro()
    {
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active));
        await _sut.SyncSubscriptionAsync("sub_1");

        await _sut.CancelSubscriptionAsync(UserId);

        _gateway.LastCancelledStripeId.Should().Be("sub_1");
        var stored = await Stored();
        stored!.GrantsPlan.Should().BeTrue();
        stored.CancelAtPeriodEnd.Should().BeTrue();
    }

    [Fact]
    public async Task CancellingOnFree_Throws()
    {
        var act = () => _sut.CancelSubscriptionAsync(UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _gateway.LastCancelledStripeId.Should().BeNull();
    }

    [Fact]
    public async Task TheCurrentPlan_OfAUserWithoutASubscription_IsFree()
    {
        (await _sut.GetSubscriptionStatusAsync(UserId)).Should().Be(new SubscriptionStatusDto("Free", "Free", null, null, false));
    }

    [Fact]
    public async Task TheCurrentPlan_OfAnActiveSubscription_IsNamedFromThePlan()
    {
        _gateway.Holds(AtStripe("sub_1", SubscriptionStatus.Active));
        await _sut.SyncSubscriptionAsync("sub_1");

        (await _sut.GetSubscriptionStatusAsync(UserId)).Should().Be(new SubscriptionStatusDto("Pro", "Pro", "Active", PeriodEnd, false));
    }
}
