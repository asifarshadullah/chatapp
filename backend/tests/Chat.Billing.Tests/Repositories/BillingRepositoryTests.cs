using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Data;
using Chat.Billing.Infrastructure.Repositories;
using Chat.Billing.Tests.Infrastructure;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Billing.Tests.Repositories;

/// <summary>
/// Billing documents keep their ids as strings, like Chat's and Identity's, and still read when a
/// newer version of the app has added fields to them.
/// </summary>
public class BillingRepositoryTests : IClassFixture<MongoDbFixture>
{
    private readonly IMongoDatabase _db;

    public BillingRepositoryTests(MongoDbFixture fixture) => _db = fixture.Database;

    private IMongoCollection<BsonDocument> Raw(string name) => _db.GetCollection<BsonDocument>(name);

    // ── Plans ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APlan_IsReadBackWithEveryField_AndFoundByTier()
    {
        var id = Guid.NewGuid();
        await _db.GetCollection<PlanDocument>("plans").InsertOneAsync(new PlanDocument
        {
            Id = id, Name = "Pro", Tier = PlanTier.Pro, PricePerMonth = 19m, DailyMessageLimit = 100, Features = [Feature.Chat],
        });
        var repository = new MongoPlanRepository(_db);

        var byId = await repository.GetByIdAsync(id);
        var byTier = await repository.GetByTierAsync(PlanTier.Pro);

        byId.Should().BeEquivalentTo(new Plan(id, "Pro", PlanTier.Pro, 19m, 100, [Feature.Chat]));
        byTier!.Id.Should().Be(id);
    }

    [Fact]
    public async Task APlan_StoresItsIdAsAString()
    {
        var id = Guid.NewGuid();
        await _db.GetCollection<PlanDocument>("plans_ids").InsertOneAsync(new PlanDocument { Id = id, Name = "Free" });

        var raw = await Raw("plans_ids").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        raw["_id"].Should().Be(new BsonString(id.ToString()));
    }

    [Fact]
    public async Task APlanWithAFieldThisVersionDoesNotKnow_StillReads()
    {
        var id = Guid.NewGuid();
        await Raw("plans").InsertOneAsync(new BsonDocument
        {
            { "_id", id.ToString() }, { "Name", "Free" }, { "Tier", (int)PlanTier.Free }, { "PricePerMonth", "0" },
            { "DailyMessageLimit", 10 }, { "Features", new BsonArray { (int)Feature.Chat } }, { "AddedLater", true },
        });

        var plan = await new MongoPlanRepository(_db).GetByIdAsync(id);

        plan!.Name.Should().Be("Free");
    }

    // ── Subscriptions ────────────────────────────────────────────────────────

    [Fact]
    public async Task ASubscription_IsReadBackWithEveryField_AndStoresItsIdsAsStrings()
    {
        var userId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var periodEnd = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new Subscription(Guid.NewGuid(), userId, planId, SubscriptionStatus.Active, periodEnd, "sub_1");
        var repository = new MongoSubscriptionRepository(_db);

        await repository.SaveAsync(subscription);

        (await repository.GetByUserIdAsync(userId)).Should().BeEquivalentTo(subscription);
        var raw = await Raw("subscriptions").Find(new BsonDocument("StripeSubscriptionId", "sub_1")).SingleAsync();
        raw["_id"].Should().Be(new BsonString(subscription.Id.ToString()));
        raw["UserId"].Should().Be(new BsonString(userId.ToString()));
        raw["PlanId"].Should().Be(new BsonString(planId.ToString()));
    }

    [Fact]
    public async Task ASubscriptionWithAFieldThisVersionDoesNotKnow_StillReads()
    {
        var userId = Guid.NewGuid();
        await Raw("subscriptions").InsertOneAsync(new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() }, { "UserId", userId.ToString() }, { "PlanId", Guid.NewGuid().ToString() },
            { "Status", (int)SubscriptionStatus.Active }, { "CurrentPeriodEnd", DateTime.UtcNow },
            { "StripeSubscriptionId", "sub_extra" }, { "AddedLater", true },
        });

        var subscription = await new MongoSubscriptionRepository(_db).GetByUserIdAsync(userId);

        subscription!.StripeSubscriptionId.Should().Be("sub_extra");
    }
}
