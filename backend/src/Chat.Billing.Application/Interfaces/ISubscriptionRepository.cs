using Chat.Billing.Domain.Entities;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Persistence interface for Subscription entities.</summary>
public interface ISubscriptionRepository
{
    /// <summary>Returns the subscription for a user, or null if none exists.</summary>
    Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns the subscription associated with a Stripe subscription ID, or null.</summary>
    Task<Subscription?> GetByStripeIdAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Inserts or updates the subscription.</summary>
    Task SaveAsync(Subscription subscription, CancellationToken ct = default);
}
