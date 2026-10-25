using System.Net;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// The authentication middleware asks every remote sign-in handler about every request, so a
/// Google handler without a client id would fail the whole app, not just Google sign-in.
/// </summary>
public class GoogleNotConfiguredTests : IDisposable
{
    private readonly AuthApiFactory _factory = new();
    private readonly HttpClient _client;

    public GoogleNotConfiguredTests()
    {
        _client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Google:ClientId", string.Empty);
            builder.UseSetting("Google:ClientSecret", string.Empty);
        }).CreateClient();
    }

    [Fact]
    public async Task OtherRequestsStillWork()
    {
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GoogleSignIn_IsNotFound()
    {
        var response = await _client.GetAsync("/auth/google");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose() => _factory.Dispose();
}
