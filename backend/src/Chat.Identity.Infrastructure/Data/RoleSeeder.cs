using MongoDB.Driver;

namespace Chat.Identity.Infrastructure.Data;

/// <summary>
/// Seeds default role documents into the roles collection on startup if it is empty.
/// Adding a new role or changing permissions requires only a database update — no code change.
/// </summary>
public static class RoleSeeder
{
    public static async Task SeedAsync(IMongoDatabase database, CancellationToken ct = default)
    {
        var collection = database.GetCollection<RoleDocument>("roles");

        if (await collection.CountDocumentsAsync(FilterDefinition<RoleDocument>.Empty, cancellationToken: ct) > 0)
            return;

        var defaults = new[]
        {
            new RoleDocument { Name = "User",     Permissions = ["conversation:create", "conversation:read"] },
            new RoleDocument { Name = "OrgAdmin", Permissions = ["conversation:create", "conversation:read", "conversation:share", "user:invite"] },
            new RoleDocument { Name = "Admin",    Permissions = ["*"] },
        };

        await collection.InsertManyAsync(defaults, cancellationToken: ct);
    }
}
