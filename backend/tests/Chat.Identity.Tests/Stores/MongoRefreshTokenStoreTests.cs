using Chat.Identity.Domain.Entities;
using Chat.Identity.Infrastructure.Stores;
using FluentAssertions;
using MongoDB.Driver;

namespace Chat.Identity.Tests.Stores;

/// <summary>
/// Fixture that creates a unique MongoDB database per test run and drops it on dispose.
/// Requires Docker Compose MongoDB to be running (docker compose up -d).
/// </summary>
public class RefreshTokenDbFixture : IDisposable
{
    private const string ConnectionString =
        "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";

    public IMongoDatabase Database { get; }
    private readonly string _databaseName;
    private readonly MongoClient _client;

    public RefreshTokenDbFixture()
    {
        _databaseName = $"chatapp_test_{Guid.NewGuid():N}";
        _client = new MongoClient(ConnectionString);
        Database = _client.GetDatabase(_databaseName);
    }

    public void Dispose() => _client.DropDatabase(_databaseName);
}

/// <summary>
/// Integration tests for MongoRefreshTokenStore against a real MongoDB instance.
/// </summary>
public class MongoRefreshTokenStoreTests : IClassFixture<RefreshTokenDbFixture>
{
    private readonly MongoRefreshTokenStore _sut;
    private readonly RefreshTokenDbFixture _fixture;

    public MongoRefreshTokenStoreTests(RefreshTokenDbFixture fixture)
    {
        _fixture = fixture;
        _sut = new MongoRefreshTokenStore(fixture.Database);
    }

    private static RefreshToken NewToken(string hash, Guid? familyId = null, Guid? userId = null)
        => new(hash, userId ?? Guid.NewGuid(), familyId ?? Guid.NewGuid(),
            DateTime.UtcNow.AddDays(14));

    // ── Task 4.1 — round-trip ────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenFindByHash_ReturnsTheToken()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}");

        await _sut.AddAsync(token);
        var found = await _sut.FindByHashAsync(token.TokenHash);

        found.Should().NotBeNull();
        found!.Id.Should().Be(token.Id);
        found.UserId.Should().Be(token.UserId);
        found.FamilyId.Should().Be(token.FamilyId);
        found.ExpiresAt.Should().BeCloseTo(token.ExpiresAt, TimeSpan.FromMilliseconds(1));
        found.IsUsable(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task FindByHashAsync_WithAnUnknownHash_ReturnsNull()
    {
        var found = await _sut.FindByHashAsync($"never-stored-{Guid.NewGuid():N}");

        found.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_PersistsConsumptionState()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}");
        await _sut.AddAsync(token);

        token.Consume(DateTime.UtcNow);
        await _sut.UpdateAsync(token);

        var found = await _sut.FindByHashAsync(token.TokenHash);
        found!.ConsumedAt.Should().NotBeNull();
        found.IsUsable(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public async Task Reload_OfAConsumedAndRevokedToken_KeepsBothTimestamps()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}");
        token.Consume(DateTime.UtcNow);
        token.Revoke(DateTime.UtcNow);
        await _sut.AddAsync(token);

        // Reconstruction must not trip the guard on Consume().
        var found = await _sut.FindByHashAsync(token.TokenHash);

        found!.ConsumedAt.Should().NotBeNull();
        found.RevokedAt.Should().NotBeNull();
    }

    // ── Task 4.2 — family revocation ─────────────────────────────────────────

    [Fact]
    public async Task RevokeFamilyAsync_RevokesEveryMemberOfTheFamily()
    {
        var familyId = Guid.NewGuid();
        var first = NewToken($"hash-{Guid.NewGuid():N}", familyId);
        var second = NewToken($"hash-{Guid.NewGuid():N}", familyId);
        await _sut.AddAsync(first);
        await _sut.AddAsync(second);

        await _sut.RevokeFamilyAsync(familyId, DateTime.UtcNow);

        (await _sut.FindByHashAsync(first.TokenHash))!.RevokedAt.Should().NotBeNull();
        (await _sut.FindByHashAsync(second.TokenHash))!.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeFamilyAsync_LeavesOtherFamiliesAlone()
    {
        var doomed = NewToken($"hash-{Guid.NewGuid():N}");
        var survivor = NewToken($"hash-{Guid.NewGuid():N}");
        await _sut.AddAsync(doomed);
        await _sut.AddAsync(survivor);

        await _sut.RevokeFamilyAsync(doomed.FamilyId, DateTime.UtcNow);

        (await _sut.FindByHashAsync(survivor.TokenHash))!.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RevokeFamilyAsync_KeepsTheOriginalRevocationTime()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}");
        await _sut.AddAsync(token);
        var firstRevocation = DateTime.UtcNow;

        await _sut.RevokeFamilyAsync(token.FamilyId, firstRevocation);
        await _sut.RevokeFamilyAsync(token.FamilyId, firstRevocation.AddHours(1));

        // Matches the entity's idempotent Revoke: the first revocation is the one that counts.
        var found = await _sut.FindByHashAsync(token.TokenHash);
        found!.RevokedAt.Should().BeCloseTo(firstRevocation, TimeSpan.FromSeconds(1));
    }

    // ── Task 4.3 — indexes ───────────────────────────────────────────────────

    [Fact]
    public async Task Collection_HasAUniqueIndexOnTheTokenHash()
    {
        var indexes = await ListIndexes();

        var hashIndex = indexes.Single(i => i["name"] == "tokenHash_1");
        hashIndex.Contains("unique").Should().BeTrue();
        hashIndex["unique"].AsBoolean.Should().BeTrue();
    }

    [Fact]
    public async Task Collection_ReapsExpiredTokensViaATtlIndex()
    {
        var indexes = await ListIndexes();

        var ttlIndex = indexes.Single(i => i["name"] == "expiresAt_ttl");
        ttlIndex.Contains("expireAfterSeconds").Should().BeTrue();
        ttlIndex["expireAfterSeconds"].ToDouble().Should()
            .Be(MongoRefreshTokenStore.RetentionAfterExpiry.TotalSeconds);
    }

    [Fact]
    public async Task DuplicateTokenHash_IsRejectedByTheDatabase()
    {
        var hash = $"hash-{Guid.NewGuid():N}";
        await _sut.AddAsync(NewToken(hash));

        var act = () => _sut.AddAsync(NewToken(hash));

        await act.Should().ThrowAsync<MongoWriteException>();
    }

    private async Task<List<MongoDB.Bson.BsonDocument>> ListIndexes()
    {
        var cursor = await _fixture.Database
            .GetCollection<RefreshTokenDocument>("refreshTokens")
            .Indexes.ListAsync();
        return await cursor.ToListAsync();
    }
}
