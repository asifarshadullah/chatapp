using System.Net;
using System.Net.Http.Json;
using Chat.Identity.Application.DTOs;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Integration tests for the refresh cookie and the /auth/refresh and /auth/logout endpoints.
/// The exchange rules themselves live in IdentityServiceRefreshTests; what is checked here is
/// the HTTP surface — that the credential travels as an http-only cookie and never in a body.
/// </summary>
public class RefreshEndpointTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public RefreshEndpointTests(AuthApiFactory factory) => _factory = factory;

    /// <summary>A client that does not follow redirects and keeps cookies out of the way.</summary>
    private HttpClient NewClient() => _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing
        .WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<(string SetCookie, HttpResponseMessage Response)> Register(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/register",
            new { Email = "cookie@example.com", Password = "Password123!", DisplayName = "Cookie" });
        return (response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values) : string.Empty, response);
    }

    private static string CookieValueOf(string setCookieHeader)
        => setCookieHeader.Split(';')[0].Split('=', 2)[1];

    // ── Task 5.1 — issuance as an http-only cookie ───────────────────────────

    [Fact]
    public async Task Register_SetsAnHttpOnlyRefreshCookie()
    {
        var (setCookie, response) = await Register(NewClient());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        setCookie.Should().Contain("refresh_token=");
        setCookie.Should().Contain("httponly", Exactly.Once());
    }

    [Fact]
    public async Task Login_SetsAnHttpOnlyRefreshCookie()
    {
        var response = await NewClient().PostAsJsonAsync("/auth/login",
            new { Email = "login@example.com", Password = "Password123!" });

        response.Headers.GetValues("Set-Cookie").Should()
            .ContainSingle(v => v.Contains("refresh_token=") && v.ToLowerInvariant().Contains("httponly"));
    }

    [Fact]
    public async Task Register_DoesNotPutTheRefreshTokenInTheResponseBody()
    {
        var client = NewClient();
        var (setCookie, response) = await Register(client);
        var rawToken = CookieValueOf(setCookie);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(rawToken);
        body.ToLowerInvariant().Should().NotContain("refreshtoken");
    }

    [Fact]
    public async Task Register_StillReturnsTheAccessTokenInTheBody()
    {
        var (_, response) = await Register(NewClient());

        var dto = await response.Content.ReadFromJsonAsync<TokenDto>();
        dto!.AccessToken.Should().NotBeNullOrWhiteSpace();
        dto.UserId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RefreshCookie_IsScopedToTheAuthEndpoints()
    {
        var (setCookie, _) = await Register(NewClient());

        setCookie.ToLowerInvariant().Should().Contain("path=/auth");
        setCookie.ToLowerInvariant().Should().Contain("samesite=lax");
    }

    // ── Task 5.2 — exchange ──────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithTheCookie_ReturnsANewAccessTokenAndRotatesTheCookie()
    {
        var client = NewClient();
        var (setCookie, _) = await Register(client);
        var original = CookieValueOf(setCookie);

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"refresh_token={original}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<TokenDto>();
        dto!.AccessToken.Should().NotBeNullOrWhiteSpace();

        var rotated = CookieValueOf(string.Join("; ", response.Headers.GetValues("Set-Cookie")));
        rotated.Should().NotBe(original);
    }

    // ── Task 5.3 — refusal ───────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithoutACookie_IsUnauthorized()
    {
        var response = await NewClient().PostAsync("/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithAnUnknownCookie_IsUnauthorized()
    {
        var client = NewClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", "refresh_token=never-issued");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReplayingAConsumedCookie_IsUnauthorized()
    {
        var client = NewClient();
        var (setCookie, _) = await Register(client);
        var original = CookieValueOf(setCookie);

        var first = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        first.Headers.Add("Cookie", $"refresh_token={original}");
        await client.SendAsync(first);

        var replay = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        replay.Headers.Add("Cookie", $"refresh_token={original}");
        var response = await client.SendAsync(replay);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_DoesNotRequireAnAccessToken()
    {
        // The whole point is renewing when the access token has already lapsed, so the
        // endpoint must not sit behind [Authorize].
        var client = NewClient();
        var (setCookie, _) = await Register(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"refresh_token={CookieValueOf(setCookie)}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Task 5.4 — sign-out ──────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ClearsTheCookie()
    {
        var client = NewClient();
        var (setCookie, _) = await Register(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        request.Headers.Add("Cookie", $"refresh_token={CookieValueOf(setCookie)}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cleared = string.Join("; ", response.Headers.GetValues("Set-Cookie"));
        cleared.Should().Contain("refresh_token=");
        // An expiry in the past is how a cookie is removed.
        cleared.ToLowerInvariant().Should().Contain("expires=");
    }

    [Fact]
    public async Task Logout_ThenRefresh_IsUnauthorized()
    {
        var client = NewClient();
        var (setCookie, _) = await Register(client);
        var token = CookieValueOf(setCookie);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logout.Headers.Add("Cookie", $"refresh_token={token}");
        await client.SendAsync(logout);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        refresh.Headers.Add("Cookie", $"refresh_token={token}");
        var response = await client.SendAsync(refresh);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutACookie_Succeeds()
    {
        var response = await NewClient().PostAsync("/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
