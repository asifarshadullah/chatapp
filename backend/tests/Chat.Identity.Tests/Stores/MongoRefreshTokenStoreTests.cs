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

    private static RefreshToken NewToken(string hash, Guid? familyId = null, Guid? userId = null,
        bool persistent = false)
        => new(hash, userId ?? Guid.NewGuid(), familyId ?? Guid.NewGuid(),
            DateTime.UtcNow.AddDays(14), persistent);

    // ── Task 2.2/2.3 — the session's chosen length round-trips ───────────────

    [Fact]
    public async Task AddAsync_ThenFindByHash_KeepsPersistence()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}", persistent: true);

        await _sut.AddAsync(token);

        (await _sut.FindByHashAsync(token.TokenHash))!.Persistent.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_ThenFindByHash_KeepsTheAbsenceOfPersistence()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}");

        await _sut.AddAsync(token);

        (await _sut.FindByHashAsync(token.TokenHash))!.Persistent.Should().BeFalse();
    }

    [Fact]
    public async Task ADocumentStoredBeforePersistenceExisted_ReadsAsNotPersistent()
    {
        // Exactly the shape written by the previous version of the schema: no Persistent
        // field at all. Such a credential was issued under the ordinary lifetime and must
        // not be promoted to a remembered one by the deploy that adds the field.
        var token = NewToken($"hash-{Guid.NewGuid():N}", persistent: true);
        await _sut.AddAsync(token);
        await _fixture.Database.GetCollection<MongoDB.Bson.BsonDocument>("refreshTokens")
            .UpdateOneAsync(
                new MongoDB.Bson.BsonDocument("TokenHash", token.TokenHash),
                new MongoDB.Bson.BsonDocument("$unset",
                    new MongoDB.Bson.BsonDocument("Persistent", "")));

        var found = await _sut.FindByHashAsync(token.TokenHash);

        found.Should().NotBeNull();
        found!.Persistent.Should().BeFalse();
    }

    // ── Task 7.3 — a consumed credential is reaped on its own schedule ───────

    [Fact]
    public async Task Consuming_PersistsThePulledInExpiry()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}", persistent: true);
        await _sut.AddAsync(token);

        token.Consume(DateTime.UtcNow);
        await _sut.UpdateAsync(token);

        // The TTL index reaps on ExpiresAt, so the pulled-in value has to reach storage —
        // otherwise the record still sits there for the whole of the session's lifetime.
        var found = await _sut.FindByHashAsync(token.TokenHash);
        found!.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        found.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(1));
    }

    [Fact]
    public async Task AConsumedCredentialIsStillFoundWithinTheRetentionWindow()
    {
        var token = NewToken($"hash-{Guid.NewGuid():N}", persistent: true);
        await _sut.AddAsync(token);
        token.Consume(DateTime.UtcNow);
        await _sut.UpdateAsync(token);

        // Replay detection depends on the record outliving the credential. The retention
        // margin, not the session lifetime, is what buys that time.
        var found = await _sut.FindByHashAsync(token.TokenHash);

        found.Should().NotBeNull();
        found!.ConsumedAt.Should().NotBeNull();
        MongoRefreshTokenStore.RetentionAfterExpiry.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ReplayingAConsumedCredential_StillRevokesTheFamily()
    {
        var familyId = Guid.NewGuid();
        var consumed = NewToken($"hash-{Guid.NewGuid():N}", familyId, persistent: true);
        var successor = NewToken($"hash-{Guid.NewGuid():N}", familyId, persistent: true);
        await _sut.AddAsync(consumed);
        await _sut.AddAsync(successor);
        consumed.Consume(DateTime.UtcNow);
        await _sut.UpdateAsync(consumed);

        var found = await _sut.FindByHashAsync(consumed.TokenHash);
        await _sut.RevokeFamilyAsync(found!.FamilyId, DateTime.UtcNow);

        (await _sut.FindByHashAsync(successor.TokenHash))!.RevokedAt.Should().NotBeNull();
    }

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
