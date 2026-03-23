namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Checks whether the current user's role grants a given permission string.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Returns true if the user's role grants the given permission.
    /// Admin role with wildcard "*" grants all permissions.
    /// </summary>
    Task<bool> IsAuthorizedAsync(Guid userId, string permission, CancellationToken ct = default);
}
