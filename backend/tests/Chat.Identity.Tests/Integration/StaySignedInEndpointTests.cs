using System.Net.Http.Json;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Tasks 4.1/4.2 — the HTTP surface of the "keep me signed in" choice: the flag reaches the
/// service, and the cookie is retained across browser restarts only when it was chosen.
/// </summary>
public class StaySignedInEndpointTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public StaySignedInEndpointTests(AuthApiFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private FakeIdentityService Service()
        => (FakeIdentityService)_factory.Services.GetRequiredService<IIdentityService>();

    private static string RefreshCookie(HttpResponseMessage response)
        => response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("refresh_token="));

    // ── Task 4.1 — the flag reaches the service ──────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_PassesTheChoiceToTheService(bool staySignedIn)
    {
        await NewClient().PostAsJsonAsync("/auth/login", new
        {
            Email = "login@example.com",
            Password = "Password123!",
            StaySignedIn = staySignedIn
        });

        Service().LastStaySignedIn.Should().Be(staySignedIn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Register_PassesTheChoiceToTheService(bool staySignedIn)
    {
        await NewClient().PostAsJsonAsync("/auth/register", new
        {
            Email = "reg@example.com",
            Password = "Password123!",
            DisplayName = "Reg",
            StaySignedIn = staySignedIn
        });

        Service().LastStaySignedIn.Should().Be(staySignedIn);
    }

    [Fact]
    public async Task ARequestOmittingTheChoiceIsAnOrdinarySession()
    {
        // Compatibility: a client that has not been updated must still get a working,
        // short session rather than a rejected request.
        var response = await NewClient().PostAsJsonAsync("/auth/login",
            new { Email = "login@example.com", Password = "Password123!" });

        response.IsSuccessStatusCode.Should().BeTrue();
        Service().LastStaySignedIn.Should().BeFalse();
    }

    // ── Task 4.2 — the cookie's retention follows the choice ─────────────────

    [Fact]
    public async Task ARememberedSessionGetsACookieThatOutlivesTheBrowser()
    {
        var response = await NewClient().PostAsJsonAsync("/auth/login", new
        {
            Email = "login@example.com",
            Password = "Password123!",
            StaySignedIn = true
        });

        var cookie = RefreshCookie(response);
        cookie.Should().Contain("expires=", Exactly.Once(), "a remembered session survives a restart");
        ExpiryOf(cookie).Should().BeCloseTo(
            DateTimeOffset.UtcNow.Add(FakeIdentityService.RememberedLifetime), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task AnOrdinarySessionGetsABrowserSessionCookie()
    {
        var response = await NewClient().PostAsJsonAsync("/auth/login", new
        {
            Email = "login@example.com",
            Password = "Password123!",
            StaySignedIn = false
        });

        // No Expires and no Max-Age is what makes the browser drop it on close, which is
        // the whole point of declining to be remembered on a shared machine.
        var cookie = RefreshCookie(response).ToLowerInvariant();
        cookie.Should().NotContain("expires=");
        cookie.Should().NotContain("max-age=");
    }

    [Fact]
    public async Task RefreshKeepsTheSessionsRetention()
    {
        var client = NewClient();
        var login = await client.PostAsJsonAsync("/auth/login", new
        {
            Email = "login@example.com",
            Password = "Password123!",
            StaySignedIn = true
        });
        var raw = RefreshCookie(login).Split(';')[0].Split('=', 2)[1];

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"refresh_token={raw}");
        var refreshed = await client.SendAsync(request);

        RefreshCookie(refreshed).Should().Contain("expires=");
    }

    [Fact]
    public async Task RefreshOfAnOrdinarySessionStaysABrowserSessionCookie()
    {
        var client = NewClient();
        var login = await client.PostAsJsonAsync("/auth/login",
            new { Email = "login@example.com", Password = "Password123!", StaySignedIn = false });
        var raw = RefreshCookie(login).Split(';')[0].Split('=', 2)[1];

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"refresh_token={raw}");
        var refreshed = await client.SendAsync(request);

        RefreshCookie(refreshed).ToLowerInvariant().Should().NotContain("expires=");
    }

    private static DateTimeOffset ExpiryOf(string setCookie)
    {
        var part = setCookie.Split(';')
            .Select(p => p.Trim())
            .Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return DateTimeOffset.Parse(part["expires=".Length..],
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
