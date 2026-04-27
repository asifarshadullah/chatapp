using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Data;
using MongoDB.Driver;

namespace Chat.Billing.Infrastructure.Repositories;

/// <summary>MongoDB-backed implementation of <see cref="ISubscriptionRepository"/>.</summary>
public class MongoSubscriptionRepository : ISubscriptionRepository
{
    private readonly IMongoCollection<SubscriptionDocument> _collection;

    public MongoSubscriptionRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<SubscriptionDocument>("subscriptions");
    }

    /// <inheritdoc />
    public async Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(d => d.UserId == userId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToEntity(doc);
    }

    /// <inheritdoc />
    public async Task<Subscription?> GetByStripeIdAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(d => d.StripeSubscriptionId == stripeSubscriptionId)
            .FirstOrDefaultAsync(ct);
        return doc is null ? null : ToEntity(doc);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Subscription subscription, CancellationToken ct = default)
    {
        var doc = ToDocument(subscription);
        var options = new ReplaceOptions { IsUpsert = true };
        await _collection.ReplaceOneAsync(d => d.Id == doc.Id, doc, options, ct);
    }

    private static Subscription ToEntity(SubscriptionDocument doc) =>
        new(doc.Id, doc.UserId, doc.PlanId, doc.Status, doc.CurrentPeriodEnd, doc.StripeSubscriptionId);

    private static SubscriptionDocument ToDocument(Subscription s) =>
        new()
        {
            Id = s.Id,
            UserId = s.UserId,
            PlanId = s.PlanId,
            Status = s.Status,
            CurrentPeriodEnd = s.CurrentPeriodEnd,
            StripeSubscriptionId = s.StripeSubscriptionId,
        };
}
