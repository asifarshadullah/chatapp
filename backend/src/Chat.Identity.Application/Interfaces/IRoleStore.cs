namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Permissions assigned to a role. Returned by IRoleStore lookups.
/// </summary>
public record RoleInfo(string Name, IReadOnlyList<string> Permissions);

/// <summary>
/// Read-only access to role documents from the roles collection.
/// </summary>
public interface IRoleStore
{
    Task<RoleInfo?> GetByNameAsync(string name, CancellationToken ct = default);
}
