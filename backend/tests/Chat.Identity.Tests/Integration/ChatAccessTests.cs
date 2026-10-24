using System.Net;
using System.Net.Http.Json;
using Chat.Application.DTOs;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Which role permission each chat operation needs. Runs the real PermissionService against
/// seeded roles, so a policy pointed at the wrong permission, or a broken wildcard, fails here.
/// </summary>
public class ChatAccessTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public ChatAccessTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    // ── History needs conversation:read ─────────────────────────────────────

    [Fact]
    public async Task GetHistory_OwnConversation_WithoutReadPermission_Returns403AndNoMessages()
    {
        var user = Guid.NewGuid();
        var conversationId = await _factory.SeedConversationAsync(user, "My secret");
        var client = _factory.CreateAuthenticatedClientWithRole(user, AuthApiFactory.NoReadRole);

        var response = await client.GetAsync($"/api/chat/{conversationId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("My secret");
    }

    [Fact]
    public async Task GetHistory_AnotherUsersConversation_WithoutReadPermission_Returns403()
    {
        var someoneElses = await _factory.SeedConversationAsync(Guid.NewGuid(), "Their secret");
        var client = _factory.CreateAuthenticatedClientWithRole(Guid.NewGuid(), AuthApiFactory.NoReadRole);

        var response = await client.GetAsync($"/api/chat/{someoneElses}/history");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetHistory_UnknownConversation_WithoutReadPermission_Returns403()
    {
        var client = _factory.CreateAuthenticatedClientWithRole(Guid.NewGuid(), AuthApiFactory.NoReadRole);

        var response = await client.GetAsync($"/api/chat/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Admin")]
    public async Task GetHistory_OwnConversation_WithReadPermission_ReturnsEveryMessage(string role)
    {
        var user = Guid.NewGuid();
        var conversationId = await _factory.SeedConversationAsync(user, "Hello");
        var client = _factory.CreateAuthenticatedClientWithRole(user, role);

        var response = await client.GetAsync($"/api/chat/{conversationId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<ConversationHistoryDto>();
        history!.Messages.Select(m => m.Content).Should().Equal("Hello", "Seeded reply");
    }
}
