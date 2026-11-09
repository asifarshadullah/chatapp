using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Infrastructure.Data;
using MongoDB.Driver;

namespace Chat.Billing.Infrastructure.Repositories;

/// <summary>MongoDB-backed implementation of <see cref="ISubscriptionRepository"/>.</summary>
public class MongoSubscriptionRepository : ISubscriptionRepository
{
    private const string CollectionName = "subscriptions";

    private readonly IMongoCollection<SubscriptionDocument> _collection;

    public MongoSubscriptionRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<SubscriptionDocument>(CollectionName);
    }

    /// <inheritdoc />
    public async Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(d => d.UserId == userId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToEntity(doc);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One upsert keyed by user, behind a unique index, so Stripe events that arrive together for a
    /// new subscription cannot store it twice; the server retries the upsert that loses the race.
    /// </remarks>
    public Task SaveAsync(Subscription subscription, CancellationToken ct = default) =>
        _collection.UpdateOneAsync(
            d => d.UserId == subscription.UserId,
            Builders<SubscriptionDocument>.Update
                .Set(d => d.PlanId, subscription.PlanId)
                .Set(d => d.Status, subscription.Status)
                .Set(d => d.CurrentPeriodEnd, subscription.CurrentPeriodEnd)
                .Set(d => d.StripeSubscriptionId, subscription.StripeSubscriptionId)
                .Set(d => d.CancelAtPeriodEnd, subscription.CancelAtPeriodEnd)
                .SetOnInsert(d => d.Id, subscription.Id),
            new UpdateOptions { IsUpsert = true },
            ct);

    /// <summary>Makes the user unique, which keeps one subscription per user.</summary>
    public static Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct = default) =>
        database.GetCollection<SubscriptionDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<SubscriptionDocument>(
                Builders<SubscriptionDocument>.IndexKeys.Ascending(d => d.UserId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

    private static Subscription ToEntity(SubscriptionDocument doc) =>
        new(doc.Id, doc.UserId, doc.PlanId, doc.Status, doc.CurrentPeriodEnd, doc.StripeSubscriptionId, doc.CancelAtPeriodEnd);
}
