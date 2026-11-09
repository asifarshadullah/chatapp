using System.Collections.Concurrent;
using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>Free and Pro, held in memory.</summary>
public class InMemoryPlanRepository : IPlanRepository
{
    public Plan Free { get; } = new(Guid.NewGuid(), "Free", PlanTier.Free, 0m, 10, [Feature.Chat]);
    public Plan Pro { get; } = new(Guid.NewGuid(), "Pro", PlanTier.Pro, 19m, 100, [Feature.Chat]);
    public int Reads { get; private set; }

    private IEnumerable<Plan> All => [Free, Pro];

    public Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<Plan>>(All.ToList());
    }

    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(All.FirstOrDefault(p => p.Id == id));
    }

    public Task<Plan?> GetByTierAsync(PlanTier tier, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(All.FirstOrDefault(p => p.Tier == tier));
    }
}

/// <summary>Subscriptions held in memory, one per user.</summary>
public class InMemorySubscriptionRepository : ISubscriptionRepository
{
    private readonly ConcurrentDictionary<Guid, Subscription> _byUser = new();

    public Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_byUser.GetValueOrDefault(userId));

    public Task SaveAsync(Subscription subscription, CancellationToken ct = default)
    {
        _byUser.AddOrUpdate(subscription.UserId, subscription, (_, existing) => new Subscription(existing.Id, subscription.UserId,
            subscription.PlanId, subscription.Status, subscription.CurrentPeriodEnd, subscription.StripeSubscriptionId,
            subscription.CancelAtPeriodEnd));
        return Task.CompletedTask;
    }
}

/// <summary>Stands in for Stripe: records what it was asked and fails when told to.</summary>
public class FakePaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, StripeSubscriptionSnapshot> _subscriptions = new();
    private readonly ConcurrentQueue<string> _reads = new();

    public int Calls { get; private set; }

    /// <summary>The Stripe subscriptions read, in order.</summary>
    public IReadOnlyList<string> Reads => _reads.ToArray();

    public Exception? ReadFailure { get; set; }

    public void Holds(StripeSubscriptionSnapshot snapshot) => _subscriptions[snapshot.Id] = snapshot;

    public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        _reads.Enqueue(stripeSubscriptionId);
        if (ReadFailure is not null)
            throw ReadFailure;
        return _subscriptions.TryGetValue(stripeSubscriptionId, out var snapshot)
            ? Task.FromResult(snapshot)
            : throw new SubscriptionNotFoundAtGatewayException(stripeSubscriptionId);
    }
    public (Guid UserId, string PriceId, string SuccessUrl, string CancelUrl)? LastCheckout { get; private set; }
    public Exception? FailWith { get; set; }

    public Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        Calls++;
        if (FailWith is not null)
            throw FailWith;
        LastCheckout = (userId, stripePriceId, successUrl, cancelUrl);
        return Task.FromResult(new CheckoutSessionDto("https://checkout.stripe.com/c/pay/cs_test_1", "cs_test_1"));
    }

    /// <summary>The Stripe subscriptions asked to end at their period's end, in order.</summary>
    public List<string> CancelledAtPeriodEnd { get; } = [];

    public Task CancelAtPeriodEndAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        Calls++;
        CancelledAtPeriodEnd.Add(stripeSubscriptionId);
        if (_subscriptions.TryGetValue(stripeSubscriptionId, out var snapshot))
            _subscriptions[stripeSubscriptionId] = snapshot with { CancelAtPeriodEnd = true };
        return Task.CompletedTask;
    }
}
