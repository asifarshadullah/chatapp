using Chat.Identity.Application.Interfaces;
using Chat.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace Chat.Identity.Tests.Services;

/// <summary>
/// Unit tests for PermissionService.
/// Uses FakeRoleStore and FakeCurrentUser — no real DB or HttpContext needed.
/// </summary>
public class PermissionServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static PermissionService Build(out FakeRoleStore store, string role = "User")
    {
        store = new FakeRoleStore();
        var currentUser = new FakeCurrentUser(role);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new PermissionService(store, currentUser, cache);
    }

    // ── Cycle 1.1 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsAuthorizedAsync_AdminRole_ReturnsTrue_ForAnyPermission()
    {
        var svc = Build(out var store, role: "Admin");
        store.Roles.Add(new RoleInfo("Admin", ["*"]));

        var result = await svc.IsAuthorizedAsync(Guid.NewGuid(), "conversation:share");

        result.Should().BeTrue();
    }

    // ── Cycle 1.2 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsAuthorizedAsync_UserRole_ReturnsTrue_ForGrantedPermission()
    {
        var svc = Build(out var store, role: "User");
        store.Roles.Add(new RoleInfo("User", ["conversation:create", "conversation:read"]));

        var result = await svc.IsAuthorizedAsync(Guid.NewGuid(), "conversation:create");

        result.Should().BeTrue();
    }

    // ── Cycle 1.3 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsAuthorizedAsync_UserRole_ReturnsFalse_ForUnlistedPermission()
    {
        var svc = Build(out var store, role: "User");
        store.Roles.Add(new RoleInfo("User", ["conversation:create"]));

        var result = await svc.IsAuthorizedAsync(Guid.NewGuid(), "conversation:share");

        result.Should().BeFalse();
    }

    // ── Cycle 1.4 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsAuthorizedAsync_UnknownRole_ReturnsFalse()
    {
        var svc = Build(out var store, role: "Ghost");
        // FakeRoleStore has no "Ghost" role

        var result = await svc.IsAuthorizedAsync(Guid.NewGuid(), "conversation:create");

        result.Should().BeFalse();
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

public class FakeRoleStore : IRoleStore
{
    public List<RoleInfo> Roles { get; } = new();

    public Task<RoleInfo?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(Roles.FirstOrDefault(r => r.Name == name));
}

public class FakeCurrentUser : Chat.Identity.Application.Interfaces.ICurrentUser
{
    public FakeCurrentUser(string role, Guid? userId = null)
    {
        Role = role;
        UserId = userId ?? Guid.NewGuid();
    }

    public Guid UserId { get; }
    public string Email => "test@example.com";
    public string Role { get; }
    public bool IsAuthenticated => true;
}
