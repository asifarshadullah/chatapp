using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Identity.Infrastructure.Services;
using FluentAssertions;

namespace Chat.Identity.Tests.Services;

/// <summary>
/// Tasks 3.1–3.4 — a session's length is chosen at authentication and carried by the
/// credential, so rotation preserves it and nothing else can quietly change it.
/// The fake settings put the two lifetimes 14 and 60 days apart, so which one was applied
/// is unambiguous.
/// </summary>
public class IdentityServicePersistentSessionTests
{
    private static readonly TimeSpan Ordinary = TimeSpan.FromDays(14);
    private static readonly TimeSpan Remembered = TimeSpan.FromDays(60);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    private static IdentityService Build(out FakeUserStore users, out FakeRefreshTokenStore tokens)
    {
        users = new FakeUserStore();
        tokens = new FakeRefreshTokenStore();
        return new IdentityService(users, new FakeTokenGenerator(), tokens,
            new FakeRefreshTokenSettings());
    }

    private static AppUser SeedUser(FakeUserStore users, string email = "user@example.com")
    {
        var user = new AppUser(email, "User");
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("Password123!"));
        users.Users.Add(user);
        return user;
    }

    private static void ShouldLast(RefreshToken token, TimeSpan lifetime)
        => token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.Add(lifetime), Tolerance);

    // ── Task 3.1 — the choice reaches issuance on every entry point ──────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisterAsync_AppliesTheChosenLifetime(bool persistent)
    {
        var svc = Build(out _, out var tokens);

        await svc.RegisterAsync(new RegisterDto("new@example.com", "Password123!", "New", persistent));

        var issued = tokens.Tokens.Single();
        issued.Persistent.Should().Be(persistent);
        ShouldLast(issued, persistent ? Remembered : Ordinary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginAsync_AppliesTheChosenLifetime(bool persistent)
    {
        var svc = Build(out var users, out var tokens);
        SeedUser(users);

        await svc.LoginAsync(new LoginDto("user@example.com", "Password123!", persistent));

        var issued = tokens.Tokens.Single();
        issued.Persistent.Should().Be(persistent);
        ShouldLast(issued, persistent ? Remembered : Ordinary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleExternalCallbackAsync_AppliesTheChosenLifetime(bool persistent)
    {
        var svc = Build(out _, out var tokens);

        await svc.HandleExternalCallbackAsync("Google", "google-key-1",
            "oauth@example.com", "OAuth User", persistent);

        var issued = tokens.Tokens.Single();
        issued.Persistent.Should().Be(persistent);
        ShouldLast(issued, persistent ? Remembered : Ordinary);
    }

    [Fact]
    public async Task NotChoosingIsTheDefault()
    {
        var svc = Build(out _, out var tokens);

        // A caller that has not been updated to send the choice gets the shorter session.
        await svc.RegisterAsync(new RegisterDto("plain@example.com", "Password123!", "Plain"));

        tokens.Tokens.Single().Persistent.Should().BeFalse();
    }

    [Fact]
    public async Task TheChoiceIsPerAuthenticationNotPerUser()
    {
        var svc = Build(out var users, out var tokens);
        SeedUser(users);

        await svc.LoginAsync(new LoginDto("user@example.com", "Password123!", true));
        await svc.LoginAsync(new LoginDto("user@example.com", "Password123!", false));

        tokens.Tokens.Should().SatisfyRespectively(
            first => first.Persistent.Should().BeTrue(),
            second => second.Persistent.Should().BeFalse());
        ShouldLast(tokens.Tokens[0], Remembered);
        ShouldLast(tokens.Tokens[1], Ordinary);
    }

    // ── Task 3.2 — the credential's expiry travels with the session ──────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheReturnedRefreshExpiryMatchesTheStoredToken(bool persistent)
    {
        var svc = Build(out _, out var tokens);

        var session = await svc.RegisterAsync(
            new RegisterDto("new@example.com", "Password123!", "New", persistent));

        session.RefreshTokenExpiresAt.Should().Be(tokens.Tokens.Single().ExpiresAt);
        session.RefreshTokenPersistent.Should().Be(persistent);
    }

    // ── Task 3.3/3.4 — rotation preserves the choice and slides the window ───

    [Fact]
    public async Task ASuccessorOfARememberedSessionIsRemembered()
    {
        var svc = Build(out _, out var tokens);
        var session = await svc.RegisterAsync(
            new RegisterDto("new@example.com", "Password123!", "New", true));

        var refreshed = await svc.RefreshAsync(session.RefreshToken);

        var successor = tokens.Tokens.Last();
        successor.Persistent.Should().BeTrue();
        ShouldLast(successor, Remembered);
        refreshed.RefreshTokenPersistent.Should().BeTrue();
    }

    [Fact]
    public async Task ASuccessorOfAnOrdinarySessionStaysOrdinary()
    {
        var svc = Build(out _, out var tokens);
        var session = await svc.RegisterAsync(
            new RegisterDto("new@example.com", "Password123!", "New", false));

        var refreshed = await svc.RefreshAsync(session.RefreshToken);

        var successor = tokens.Tokens.Last();
        successor.Persistent.Should().BeFalse();
        ShouldLast(successor, Ordinary);
        refreshed.RefreshTokenPersistent.Should().BeFalse();
    }

    [Fact]
    public async Task ASuccessorExpiresLaterThanItsPredecessor()
    {
        var svc = Build(out _, out var tokens);
        var session = await svc.RegisterAsync(
            new RegisterDto("new@example.com", "Password123!", "New", true));
        var original = tokens.Tokens.Single();

        // The window is measured from the moment of exchange, so continued use keeps a
        // session alive rather than letting it lapse a fixed time after signing in.
        await Task.Delay(20);
        var refreshed = await svc.RefreshAsync(session.RefreshToken);

        refreshed.RefreshTokenExpiresAt.Should().BeAfter(original.ExpiresAt);
    }

    [Fact]
    public async Task ALegacyTokenWithNoChoiceRotatesIntoAnOrdinarySuccessor()
    {
        var svc = Build(out var users, out var tokens);
        var user = SeedUser(users);

        // The shape a credential issued before this capability existed reloads as.
        var generator = new FakeTokenGenerator();
        var pair = generator.GenerateRefreshToken();
        tokens.Tokens.Add(new RefreshToken(pair.TokenHash, user.Id, Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1)));

        await svc.RefreshAsync(pair.RawToken);

        var successor = tokens.Tokens.Last();
        successor.Persistent.Should().BeFalse();
        ShouldLast(successor, Ordinary);
    }
}
