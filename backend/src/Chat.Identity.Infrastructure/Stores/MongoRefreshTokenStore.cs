using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using MongoDB.Driver;

namespace Chat.Identity.Infrastructure.Stores;

/// <summary>
/// Persists RefreshToken to a MongoDB 'refreshTokens' collection.
/// Implements the IRefreshTokenStore contract defined in Application.
/// </summary>
public class MongoRefreshTokenStore : IRefreshTokenStore
{
    /// <summary>
    /// How long an expired token is kept before the TTL index reaps it. A margin rather than
    /// zero so that a replay arriving just after expiry still finds the record to revoke.
    /// </summary>
    public static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(7);

    private readonly IMongoCollection<RefreshTokenDocument> _collection;

    public MongoRefreshTokenStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<RefreshTokenDocument>("refreshTokens");
        EnsureIndexes();
    }

    public async Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var doc = await _collection.Find(t => t.TokenHash == tokenHash).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
        => _collection.InsertOneAsync(RefreshTokenDocument.FromDomain(token), cancellationToken: ct);

    public Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
        => _collection.ReplaceOneAsync(t => t.Id == token.Id,
            RefreshTokenDocument.FromDomain(token), cancellationToken: ct);

    /// <summary>
    /// Withdraws every member of a family in one update. Already-revoked members keep their
    /// original timestamp, matching the entity's idempotent Revoke.
    /// </summary>
    public Task RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default)
        => _collection.UpdateManyAsync(
            Builders<RefreshTokenDocument>.Filter.And(
                Builders<RefreshTokenDocument>.Filter.Eq(t => t.FamilyId, familyId),
                Builders<RefreshTokenDocument>.Filter.Eq(t => t.RevokedAt, null)),
            Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, revokedAt),
            cancellationToken: ct);

    /// <summary>
    /// Lookup is by token hash on every refresh, so it is indexed. The TTL index reaps
    /// expired tokens so the collection does not grow without bound as sessions turn over.
    /// </summary>
    private void EnsureIndexes()
    {
        _collection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<RefreshTokenDocument>(
                Builders<RefreshTokenDocument>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Name = "tokenHash_1", Unique = true }),

            new CreateIndexModel<RefreshTokenDocument>(
                Builders<RefreshTokenDocument>.IndexKeys.Ascending(t => t.FamilyId),
                new CreateIndexOptions { Name = "familyId_1" }),

            new CreateIndexModel<RefreshTokenDocument>(
                Builders<RefreshTokenDocument>.IndexKeys.Ascending(t => t.ExpiresAt),
                new CreateIndexOptions { Name = "expiresAt_ttl", ExpireAfter = RetentionAfterExpiry })
        });
    }
}
