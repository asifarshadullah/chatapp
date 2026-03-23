using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using MongoDB.Driver;

namespace Chat.Identity.Infrastructure.Stores;

/// <summary>
/// Persists AppUser to a MongoDB 'users' collection.
/// Implements the IUserStore contract defined in Application.
/// </summary>
public class MongoUserStore : IUserStore
{
    private readonly IMongoCollection<UserDocument> _collection;

    public MongoUserStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<UserDocument>("users");
    }

    /// <summary>Find a user by email (case-insensitive via lowercased storage).</summary>
    public async Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(u => u.Email == email.ToLowerInvariant())
            .FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    /// <summary>Find a user by their primary ID.</summary>
    public async Task<AppUser?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(u => u.Id == id)
            .FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    /// <summary>Find a user by an external provider + providerKey pair.</summary>
    public async Task<AppUser?> FindByLoginAsync(string provider, string providerKey, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(u => u.ExternalLogins.Any(l => l.Provider == provider && l.ProviderKey == providerKey))
            .FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    /// <summary>Insert a new user document.</summary>
    public async Task CreateAsync(AppUser user, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(UserDocument.FromDomain(user), null, ct);
    }

    /// <summary>Replace the existing document for this user.</summary>
    public async Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        await _collection.ReplaceOneAsync(u => u.Id == user.Id, UserDocument.FromDomain(user),
            cancellationToken: ct);
    }
}
