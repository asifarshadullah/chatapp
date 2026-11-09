using Chat.Billing.Domain.Entities;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Persistence interface for Subscription entities, one per user.</summary>
public interface ISubscriptionRepository
{
    /// <summary>Returns the subscription for a user, or null if none exists.</summary>
    Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Stores the user's subscription, replacing the one they had. The record keeps the id it was
    /// first stored with.
    /// </summary>
    Task SaveAsync(Subscription subscription, CancellationToken ct = default);
}
