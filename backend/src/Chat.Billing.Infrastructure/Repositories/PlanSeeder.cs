using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Configuration;
using Chat.Billing.Infrastructure.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Billing.Infrastructure.Repositories;

/// <summary>
/// Writes Free and Pro as configured, keeping each plan's id, and removes any other plan, so the
/// plans users see always match configuration.
/// </summary>
public static class PlanSeeder
{
    public static async Task SeedAsync(IMongoDatabase database, BillingSettings settings, CancellationToken ct = default)
    {
        // Plans written before ids were stored as strings can no longer be read.
        await database.GetCollection<BsonDocument>("plans").DeleteManyAsync(
            Builders<BsonDocument>.Filter.Not(Builders<BsonDocument>.Filter.Type("_id", BsonType.String)), ct);

        var plans = database.GetCollection<PlanDocument>("plans");
        await plans.DeleteManyAsync(p => p.Tier != PlanTier.Free && p.Tier != PlanTier.Pro, ct);
        await plans.Indexes.CreateOneAsync(
            new CreateIndexModel<PlanDocument>(Builders<PlanDocument>.IndexKeys.Ascending(p => p.Tier), new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await WriteAsync(plans, PlanTier.Free, settings.Plans.Free, ct);
        await WriteAsync(plans, PlanTier.Pro, settings.Plans.Pro, ct);
    }

    private static Task WriteAsync(IMongoCollection<PlanDocument> plans, PlanTier tier, PlanSettings plan, CancellationToken ct) =>
        plans.UpdateOneAsync(
            p => p.Tier == tier,
            Builders<PlanDocument>.Update
                .Set(p => p.Name, plan.Name)
                .Set(p => p.PricePerMonth, plan.PricePerMonth)
                .Set(p => p.DailyMessageLimit, plan.DailyMessages)
                .Set(p => p.Features, [Feature.Chat])
                .SetOnInsert(p => p.Id, Guid.NewGuid()),
            new UpdateOptions { IsUpsert = true },
            ct);
}
