using System.Net;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Integration tests for RBAC policy wiring.
/// AuthApiFactory runs the real PermissionService against a seeded FakeRoleStore —
/// no MongoDB or Ollama required.
/// </summary>
public class AuthorizationEndpointTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public AuthorizationEndpointTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    // ── Cycle 3.3 — AdminOnly policy: User role denied ────────────────────────

    [Fact]
    public async Task AdminOnly_WithUserRole_Returns403()
    {
        var client = _factory.CreateAuthenticatedClientWithRole(AuthApiFactory.TestUserId, "User");

        var response = await client.GetAsync("/auth/admin-probe");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Cycle 3.4 — AdminOnly policy: Admin role allowed ─────────────────────

    [Fact]
    public async Task AdminOnly_WithAdminRole_Returns200()
    {
        var client = _factory.CreateAuthenticatedClientWithRole(AuthApiFactory.TestUserId, "Admin");

        var response = await client.GetAsync("/auth/admin-probe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Cycle 3.2 — Unauthenticated request returns 401 ──────────────────────

    [Fact]
    public async Task AdminOnly_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/admin-probe");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
