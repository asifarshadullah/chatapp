using Chat.Identity.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Chat.Identity.Infrastructure.Services;

/// <summary>
/// Checks whether the current user's role grants a given permission.
/// Role is read from ICurrentUser.Role (already in JWT claim — no extra DB call).
/// Results are cached for 5 minutes to avoid per-request DB hits.
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly IRoleStore _roleStore;
    private readonly ICurrentUser _currentUser;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public PermissionService(IRoleStore roleStore, ICurrentUser currentUser, IMemoryCache cache)
    {
        _roleStore = roleStore;
        _currentUser = currentUser;
        _cache = cache;
    }

    /// <inheritdoc/>
    public async Task<bool> IsAuthorizedAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        var roleName = _currentUser.Role;
        var cacheKey = $"role:{roleName}";

        if (!_cache.TryGetValue(cacheKey, out var cachedRole) || cachedRole is not Application.Interfaces.RoleInfo role)
        {
            var fetched = await _roleStore.GetByNameAsync(roleName, ct);
            if (fetched is null)
                return false;

            role = fetched;
            _cache.Set(cacheKey, role, CacheTtl);
        }

        // Wildcard grants all permissions
        if (role.Permissions.Contains("*"))
            return true;

        return role.Permissions.Contains(permission);
    }
}
