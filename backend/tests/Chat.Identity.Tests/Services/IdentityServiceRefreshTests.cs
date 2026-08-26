using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Identity.Infrastructure.Services;
using FluentAssertions;

namespace Chat.Identity.Tests.Services;

/// <summary>
/// Exchange, rotation and reuse detection, against fakes — no database or signing key.
/// </summary>
public class IdentityServiceRefreshTests
{
    private static IdentityService Build(out FakeUserStore users, out FakeRefreshTokenStore tokens)
    {
        users = new FakeUserStore();
        tokens = new FakeRefreshTokenStore();
        return new IdentityService(users, new FakeTokenGenerator(), tokens,
            new FakeRefreshTokenSettings());
    }

    private static async Task<(IdentityService Svc, FakeRefreshTokenStore Tokens, TokenDto Session)>
        SignedIn()
    {
        var svc = Build(out _, out var tokens);
        var session = await svc.RegisterAsync(new RegisterDto("user@example.com", "Password123!", "User"));
        return (svc, tokens, session);
    }

    // ── Task 3.5 — issuance on every authentication path ─────────────────────

    [Fact]
    public async Task RegisterAsync_IssuesARefreshTokenAlongsideTheAccessToken()
    {
        var svc = Build(out _, out var tokens);

        var result = await svc.RegisterAsync(new RegisterDto("new@example.com", "Password123!", "New"));

        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.Tokens.Should().ContainSingle().Which.UserId.Should().Be(result.UserId);
    }

    [Fact]
    public async Task LoginAsync_IssuesARefreshTokenAlongsideTheAccessToken()
    {
        var svc = Build(out var users, out var tokens);
        var user = new AppUser("login@example.com", "Login User");
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("Password123!"));
        users.Users.Add(user);

        var result = await svc.LoginAsync(new LoginDto("login@example.com", "Password123!"));

        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.Tokens.Should().ContainSingle().Which.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task HandleExternalCallbackAsync_IssuesARefreshToken()
    {
        var svc = Build(out _, out var tokens);

        var result = await svc.HandleExternalCallbackAsync(
            "Google", "google-key-1", "oauth@example.com", "OAuth User");

        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.Tokens.Should().ContainSingle().Which.UserId.Should().Be(result.UserId);
    }

    // ── Task 3.1 — exchange and refusal ──────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_WithAValidToken_ReturnsANewAccessToken()
    {
        var (svc, _, session) = await SignedIn();

        var refreshed = await svc.RefreshAsync(session.RefreshToken);

        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.UserId.Should().Be(session.UserId);
    }

    [Fact]
    public async Task RefreshAsync_WithAnExpiredToken_IsRefused()
    {
        var (svc, tokens, session) = await SignedIn();
        tokens.ExpireAll();

        var act = () => svc.RefreshAsync(session.RefreshToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_WithAnUnknownToken_IsRefused()
    {
        var (svc, _, _) = await SignedIn();

        var act = () => svc.RefreshAsync("a-token-that-was-never-issued");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_WithNoToken_IsRefused()
    {
        var (svc, _, _) = await SignedIn();

        var act = () => svc.RefreshAsync("");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_RefusalsAreIndistinguishable()
    {
        var (svc, tokens, session) = await SignedIn();

        var unknown = await Capture(() => svc.RefreshAsync("never-issued"));
        var missing = await Capture(() => svc.RefreshAsync(""));
        tokens.ExpireAll();
        var expired = await Capture(() => svc.RefreshAsync(session.RefreshToken));

        // The caller must not be able to tell which condition failed.
        unknown.Should().Be(expired).And.Be(missing);
    }

    // ── Task 3.2 — rotation ──────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_ConsumesThePresentedTokenAndIssuesASuccessor()
    {
        var (svc, tokens, session) = await SignedIn();
        var original = tokens.Tokens.Single();

        var refreshed = await svc.RefreshAsync(session.RefreshToken);

        refreshed.RefreshToken.Should().NotBe(session.RefreshToken);
        original.ConsumedAt.Should().NotBeNull();
        tokens.Tokens.Should().HaveCount(2);
    }

    [Fact]
    public async Task RefreshAsync_SuccessorSharesTheFamilyOfItsPredecessor()
    {
        var (svc, tokens, session) = await SignedIn();
        var original = tokens.Tokens.Single();

        await svc.RefreshAsync(session.RefreshToken);

        tokens.Tokens.Should().OnlyContain(t => t.FamilyId == original.FamilyId);
    }

    [Fact]
    public async Task RefreshAsync_SuccessorIsItselfExchangeable()
    {
        var (svc, _, session) = await SignedIn();

        var second = await svc.RefreshAsync(session.RefreshToken);
        var third = await svc.RefreshAsync(second.RefreshToken);

        third.AccessToken.Should().NotBeNullOrWhiteSpace();
        third.RefreshToken.Should().NotBe(second.RefreshToken);
    }

    // ── Task 3.3 — reuse detection ───────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_ReplayingAConsumedToken_IsRefused()
    {
        var (svc, _, session) = await SignedIn();
        await svc.RefreshAsync(session.RefreshToken);

        var act = () => svc.RefreshAsync(session.RefreshToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_ReplayingAConsumedToken_RevokesTheWholeFamily()
    {
        var (svc, tokens, session) = await SignedIn();
        var successor = await svc.RefreshAsync(session.RefreshToken);

        await Capture(() => svc.RefreshAsync(session.RefreshToken));

        // Including the newest token, which is what stops the thief — and, unavoidably,
        // the legitimate client too.
        tokens.Tokens.Should().OnlyContain(t => t.RevokedAt != null);
        var act = () => svc.RefreshAsync(successor.RefreshToken);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_RevocationIsConfinedToTheAffectedFamily()
    {
        var svc = Build(out var users, out var tokens);
        var user = new AppUser("two@example.com", "Two Sessions");
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("Password123!"));
        users.Users.Add(user);

        var sessionA = await svc.LoginAsync(new LoginDto("two@example.com", "Password123!"));
        var sessionB = await svc.LoginAsync(new LoginDto("two@example.com", "Password123!"));

        await svc.RefreshAsync(sessionA.RefreshToken);
        await Capture(() => svc.RefreshAsync(sessionA.RefreshToken));

        // The user's other session, from a separate authentication, is untouched.
        var stillWorks = await svc.RefreshAsync(sessionB.RefreshToken);
        stillWorks.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── Task 3.4 — sign-out ──────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_RevokesTheFamily()
    {
        var (svc, tokens, session) = await SignedIn();

        await svc.LogoutAsync(session.RefreshToken);

        tokens.Tokens.Should().OnlyContain(t => t.RevokedAt != null);
        var act = () => svc.RefreshAsync(session.RefreshToken);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task LogoutAsync_WithNoToken_Succeeds()
    {
        var (svc, _, _) = await SignedIn();

        var act = () => svc.LogoutAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LogoutAsync_WithAnUnknownToken_Succeeds()
    {
        var (svc, _, _) = await SignedIn();

        var act = () => svc.LogoutAsync("never-issued");

        // Signing out must not report whether the credential was real.
        await act.Should().NotThrowAsync();
    }

    private static async Task<string> Capture(Func<Task> act)
    {
        try { await act(); return "no-throw"; }
        catch (Exception ex) { return $"{ex.GetType().Name}:{ex.Message}"; }
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

public class FakeRefreshTokenStore : IRefreshTokenStore
{
    public List<RefreshToken> Tokens { get; } = new();

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
        => Task.CompletedTask; // entities are held by reference here

    public Task RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default)
    {
        foreach (var token in Tokens.Where(t => t.FamilyId == familyId))
            token.Revoke(revokedAt);
        return Task.CompletedTask;
    }

    /// <summary>Rewrites every stored token as expired, without waiting for real time.</summary>
    public void ExpireAll()
    {
        var expired = Tokens
            .Select(t => new RefreshToken(t.Id, t.TokenHash, t.UserId, t.FamilyId,
                DateTime.UtcNow.AddSeconds(-1), t.ConsumedAt, t.RevokedAt))
            .ToList();
        Tokens.Clear();
        Tokens.AddRange(expired);
    }
}

public class FakeRefreshTokenSettings : IRefreshTokenSettings
{
    public TimeSpan Lifetime => TimeSpan.FromDays(14);

    /// <summary>Deliberately far from Lifetime, so a test can tell which one was used.</summary>
    public TimeSpan PersistentLifetime => TimeSpan.FromDays(60);
}
