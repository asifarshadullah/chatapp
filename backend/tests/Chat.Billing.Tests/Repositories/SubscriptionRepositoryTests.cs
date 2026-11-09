using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Repositories;
using Chat.Billing.Tests.Infrastructure;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Billing.Tests.Repositories;

/// <summary>Each user has at most one stored subscription, however saves for them interleave.</summary>
public class SubscriptionRepositoryTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;

    public SubscriptionRepositoryTests(MongoDbFixture fixture) => _fixture = fixture;

    private static Subscription For(Guid userId, string stripeId, SubscriptionStatus status = SubscriptionStatus.Active) =>
        new(Guid.NewGuid(), userId, Guid.NewGuid(), status, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), stripeId);

    private static Task<long> CountFor(IMongoDatabase db, Guid userId) =>
        db.GetCollection<BsonDocument>("subscriptions").CountDocumentsAsync(new BsonDocument("UserId", userId.ToString()));

    [Fact]
    public async Task SavingForAUserTwice_LeavesOneRecord_WithItsFirstId()
    {
        var db = _fixture.NewDatabase();
        var repository = new MongoSubscriptionRepository(db);
        var userId = Guid.NewGuid();
        var first = For(userId, "sub_1");
        await repository.SaveAsync(first);

        await repository.SaveAsync(For(userId, "sub_1", SubscriptionStatus.PastDue));

        (await CountFor(db, userId)).Should().Be(1);
        var stored = await repository.GetByUserIdAsync(userId);
        stored!.Id.Should().Be(first.Id);
        stored.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task SavesForANewUserStartedTogether_LeaveExactlyOneRecord()
    {
        var db = _fixture.NewDatabase();
        await MongoSubscriptionRepository.EnsureIndexesAsync(db);
        var userId = Guid.NewGuid();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => new MongoSubscriptionRepository(db).SaveAsync(For(userId, "sub_1"))));

        (await CountFor(db, userId)).Should().Be(1);
    }

    [Fact]
    public async Task TheStartupIndex_MakesTheUserUnique()
    {
        var db = _fixture.NewDatabase();

        await MongoSubscriptionRepository.EnsureIndexesAsync(db);

        var indexes = await (await db.GetCollection<BsonDocument>("subscriptions").Indexes.ListAsync()).ToListAsync();
        indexes.Should().Contain(i => i["key"].AsBsonDocument.Contains("UserId") && i.Contains("unique") && i["unique"].AsBoolean);
    }
}
