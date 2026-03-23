using System.Net;
using System.Net.Http.Json;
using Chat.Identity.Application.DTOs;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Integration tests for auth endpoints. Uses AuthApiFactory (FakeIdentityService + InMemory repos)
/// so tests run without MongoDB, Ollama, or real JWT keys.
/// </summary>
public class AuthEndpointTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Phase 1 Cycle 1.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidData_ReturnsTokenDto()
    {
        var dto = new { Email = "test@example.com", Password = "Password123!", DisplayName = "Test User" };

        var response = await _client.PostAsJsonAsync("/auth/register", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.UserId.Should().NotBe(Guid.Empty);
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    // ── Phase 2 Cycle 2.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenDto()
    {
        var dto = new { Email = "login@example.com", Password = "Password123!" };

        var response = await _client.PostAsJsonAsync("/auth/login", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── Phase 3 Cycle 3.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_SendMessage_Returns401()
    {
        var request = new { Message = "Hello" };

        var response = await _client.PostAsJsonAsync("/api/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Phase 4 Cycle 4.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_Authenticated_ReturnsUserProfile()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        profile.Should().NotBeNull();
        profile!.UserId.Should().Be(AuthApiFactory.TestUserId);
        profile.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
