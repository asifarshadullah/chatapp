using System.Security.Claims;
using Chat.Api.Contracts;
using Chat.Api.Controllers;
using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Tests.Infrastructure;
using Chat.Identity.Tests.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Controller-level tests for cookie handling on paths the in-process HTTP client cannot
/// reach: the Google callback, which needs an authenticated external principal, and the
/// Secure flag, which depends on the hosting environment.
/// </summary>
public class AuthControllerCookieTests
{
    private static AuthController Build(string environmentName, out DefaultHttpContext http)
    {
        http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(new StubExternalAuthentication())
                .BuildServiceProvider()
        };

        var controller = new AuthController(
            new FakeIdentityService(),
            new FakeRefreshTokenSettings(),
            new StubEnvironment(environmentName))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return controller;
    }

    private static string SetCookieHeader(DefaultHttpContext http)
        => string.Join("; ", http.Response.Headers.SetCookie.ToArray());

    // ── Task 5.5 — the OAuth callback issues the cookie too ──────────────────

    [Fact]
    public async Task GoogleCallback_SetsTheRefreshCookie()
    {
        var controller = Build("Production", out var http);

        var result = await controller.GoogleCallback(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        SetCookieHeader(http).Should().Contain("refresh_token=");
    }

    [Fact]
    public async Task GoogleCallback_DoesNotReturnTheRefreshTokenInTheBody()
    {
        var controller = Build("Production", out _);

        var result = await controller.GoogleCallback(CancellationToken.None);

        var body = ((OkObjectResult)result.Result!).Value;
        body.Should().BeOfType<AuthResponse>();
        // AuthResponse has no refresh-token member at all, which is the point of the type.
        body!.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Refresh"));
    }

    // ── Task 5.6 — Secure depends on the environment ─────────────────────────

    [Fact]
    public async Task InProduction_TheCookieIsMarkedSecure()
    {
        var controller = Build("Production", out var http);

        await controller.Login(new LoginDto("user@example.com", "Password123!"), CancellationToken.None);

        SetCookieHeader(http).ToLowerInvariant().Should().Contain("secure");
    }

    [Fact]
    public async Task InDevelopment_TheCookieIsNotMarkedSecure()
    {
        var controller = Build("Development", out var http);

        await controller.Login(new LoginDto("user@example.com", "Password123!"), CancellationToken.None);

        // The development API is plain HTTP on localhost; a Secure cookie would never be sent.
        SetCookieHeader(http).ToLowerInvariant().Should().NotContain("secure");
    }

    [Fact]
    public async Task TheCookieIsAlwaysHttpOnlyAndScopedToAuth()
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            var controller = Build(environment, out var http);

            await controller.Login(new LoginDto("user@example.com", "Password123!"), CancellationToken.None);

            var header = SetCookieHeader(http).ToLowerInvariant();
            header.Should().Contain("httponly", $"in {environment}");
            header.Should().Contain("path=/auth", $"in {environment}");
            header.Should().Contain("samesite=lax", $"in {environment}");
        }
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>Reports a successful Google sign-in, so the callback can be exercised.</summary>
public class StubExternalAuthentication : IAuthenticationService
{
    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-provider-key"),
            new Claim(ClaimTypes.Email, "oauth@example.com"),
            new Claim(ClaimTypes.Name, "OAuth User")
        }, "ExternalCookie");

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "ExternalCookie");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    public Task ChallengeAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
    public Task ForbidAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
    public Task SignInAsync(HttpContext c, string? s, ClaimsPrincipal p, AuthenticationProperties? o) => Task.CompletedTask;
    public Task SignOutAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
}

public class StubEnvironment : IWebHostEnvironment
{
    public StubEnvironment(string environmentName) => EnvironmentName = environmentName;

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "Chat.Api";
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
