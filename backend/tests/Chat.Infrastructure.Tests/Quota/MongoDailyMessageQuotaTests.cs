using Chat.Infrastructure.Quota;
using Chat.Infrastructure.Tests.Repositories;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Infrastructure.Tests.Quota;

/// <summary>
/// The daily limit is one counter document per user and UTC day, so it survives restarts and
/// holds across instances, and parallel messages cannot both take the last place.
/// </summary>
public class MongoDailyMessageQuotaTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;
    private readonly MovableClock _clock = new(new DateTimeOffset(2026, 10, 26, 23, 30, 0, TimeSpan.Zero));

    public MongoDailyMessageQuotaTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
    }

    private MongoDailyMessageQuota Quota(int limit) => new(_fixture.Database, limit, _clock);

    [Fact]
    public async Task UpToTheLimit_IsAllowed_AndTheNextIsNot()
    {
        var quota = Quota(3);
        var user = Guid.NewGuid();

        var results = new List<bool>();
        for (var i = 0; i < 4; i++)
            results.Add(await quota.TryConsumeAsync(user));

        results.Should().Equal(true, true, true, false);
        quota.Limit.Should().Be(3);
    }

    [Fact]
    public async Task OneUsersLimit_DoesNotAffectAnother()
    {
        var quota = Quota(1);
        var first = Guid.NewGuid();
        await quota.TryConsumeAsync(first);
        (await quota.TryConsumeAsync(first)).Should().BeFalse();

        (await quota.TryConsumeAsync(Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public async Task TheLimit_ResetsAtMidnightUtc()
    {
        var quota = Quota(1);
        var user = Guid.NewGuid();
        await quota.TryConsumeAsync(user);
        (await quota.TryConsumeAsync(user)).Should().BeFalse();

        _clock.Now = _clock.Now.AddMinutes(31); // 00:01 UTC the next day

        (await quota.TryConsumeAsync(user)).Should().BeTrue();
    }

    [Fact]
    public async Task MessagesSentAtOnce_CannotPassTheLimit()
    {
        var quota = Quota(5);
        var user = Guid.NewGuid();
        for (var i = 0; i < 4; i++)
            await quota.TryConsumeAsync(user);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => quota.TryConsumeAsync(user)));

        results.Count(allowed => allowed).Should().Be(1);
    }

    [Fact]
    public async Task TheFirstMessagesOfTheDay_RacingToCreateTheCounter_AllCount()
    {
        var quota = Quota(100);
        var user = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => quota.TryConsumeAsync(user)));

        results.Should().OnlyContain(allowed => allowed);
        var counter = await _fixture.Database.GetCollection<BsonDocument>(MongoDailyMessageQuota.CollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("userId", user.ToString())).SingleAsync();
        counter["count"].AsInt32.Should().Be(10);
    }

    [Fact]
    public async Task OldDays_ExpireThroughATtlIndex()
    {
        await MongoDailyMessageQuota.EnsureIndexesAsync(_fixture.Database);

        var indexes = await (await _fixture.Database.GetCollection<BsonDocument>(MongoDailyMessageQuota.CollectionName)
            .Indexes.ListAsync()).ToListAsync();

        indexes.Should().Contain(i => i["key"].AsBsonDocument.Contains("expiresAt")
            && i.Contains("expireAfterSeconds") && i["expireAfterSeconds"].ToInt32() == 0);
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
