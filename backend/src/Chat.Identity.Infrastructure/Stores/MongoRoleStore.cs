using Chat.Identity.Application.Interfaces;
using Chat.Identity.Infrastructure.Data;
using MongoDB.Driver;

namespace Chat.Identity.Infrastructure.Stores;

/// <summary>
/// Queries the MongoDB roles collection and maps documents to RoleInfo.
/// </summary>
public class MongoRoleStore : IRoleStore
{
    private readonly IMongoCollection<RoleDocument> _collection;

    public MongoRoleStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<RoleDocument>("roles");
    }

    /// <inheritdoc/>
    public async Task<RoleInfo?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(r => r.Name == name)
            .FirstOrDefaultAsync(ct);

        return doc?.ToRoleInfo();
    }
}
