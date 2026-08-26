using System.Security.Claims;
using Chat.Api.Controllers;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Task 4.3 — the choice made on the sign-in form has to survive a redirect out to Google
/// and back. It travels in the authentication properties, which the framework round-trips
/// inside its own encrypted, signed state, so a crafted callback cannot forge a longer
/// session than the user asked for.
/// </summary>
public class GoogleStaySignedInTests
{
    private const string ChoiceKey = "stay_signed_in";

    private static AuthController Build(FakeIdentityService identity,
        RecordingExternalAuthentication authentication, out DefaultHttpContext http)
    {
        http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(authentication)
                .AddSingleton<IUrlHelperFactory, StubUrlHelperFactory>()
                .BuildServiceProvider()
        };

        return new AuthController(identity, new StubEnvironment("Production"))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = http,
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };
    }

    // ── The challenge carries the choice ─────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GoogleLogin_CarriesTheChoiceInTheAuthenticationProperties(bool staySignedIn)
    {
        var controller = Build(new FakeIdentityService(), new RecordingExternalAuthentication(), out _);

        var result = controller.GoogleLogin(staySignedIn);

        var challenge = result.Should().BeOfType<ChallengeResult>().Subject;
        challenge.Properties!.Items[ChoiceKey].Should().Be(staySignedIn.ToString());
    }

    [Fact]
    public void GoogleLogin_DefaultsToNotStayingSignedIn()
    {
        var controller = Build(new FakeIdentityService(), new RecordingExternalAuthentication(), out _);

        var result = (ChallengeResult)controller.GoogleLogin();

        result.Properties!.Items[ChoiceKey].Should().Be(bool.FalseString);
    }

    // ── The callback honours what came back ──────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GoogleCallback_HonoursTheChoiceThatWasCarried(bool staySignedIn)
    {
        var identity = new FakeIdentityService();
        var controller = Build(identity,
            new RecordingExternalAuthentication(staySignedIn.ToString()), out var http);

        await controller.GoogleCallback(CancellationToken.None);

        identity.LastStaySignedIn.Should().Be(staySignedIn);
        var cookie = string.Join("; ", http.Response.Headers.SetCookie.ToArray()).ToLowerInvariant();
        cookie.Contains("expires=").Should().Be(staySignedIn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    public async Task GoogleCallback_WithNoReadableChoice_IssuesAnOrdinarySession(string? carried)
    {
        var identity = new FakeIdentityService();
        var controller = Build(identity, new RecordingExternalAuthentication(carried), out _);

        await controller.GoogleCallback(CancellationToken.None);

        // Anything the framework did not put there itself is not the user's choice.
        identity.LastStaySignedIn.Should().BeFalse();
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>
/// Reports a successful Google sign-in whose ticket carries the given choice, standing in
/// for the round trip the framework performs through its own state.
/// </summary>
public class RecordingExternalAuthentication : IAuthenticationService
{
    private readonly string? _carriedChoice;

    public RecordingExternalAuthentication(string? carriedChoice = null)
        => _carriedChoice = carriedChoice;

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-provider-key"),
            new Claim(ClaimTypes.Email, "oauth@example.com"),
            new Claim(ClaimTypes.Name, "OAuth User")
        }, "ExternalCookie");

        var properties = new AuthenticationProperties();
        if (_carriedChoice is not null) properties.Items["stay_signed_in"] = _carriedChoice;

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity), properties, "ExternalCookie");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    public Task ChallengeAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
    public Task ForbidAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
    public Task SignInAsync(HttpContext c, string? s, ClaimsPrincipal p, AuthenticationProperties? o) => Task.CompletedTask;
    public Task SignOutAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
}

/// <summary>Supplies a URL helper so the challenge can resolve its callback route.</summary>
public class StubUrlHelperFactory : IUrlHelperFactory
{
    public IUrlHelper GetUrlHelper(ActionContext context) => new StubUrlHelper(context);
}

public class StubUrlHelper : IUrlHelper
{
    public StubUrlHelper(ActionContext actionContext) => ActionContext = actionContext;

    public ActionContext ActionContext { get; }
    public string? Action(UrlActionContext actionContext) => "/auth/callback/google";
    public string? Content(string? contentPath) => contentPath;
    public bool IsLocalUrl(string? url) => true;
    public string? Link(string? routeName, object? values) => "/auth/callback/google";
    public string? RouteUrl(UrlRouteContext routeContext) => "/auth/callback/google";
}
