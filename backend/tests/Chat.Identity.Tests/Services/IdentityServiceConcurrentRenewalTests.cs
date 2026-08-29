using Chat.Identity.Application.DTOs;
using Chat.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.Identity.Tests.Services;

/// <summary>
/// Groups 4 and 5 — concurrent renewal. Every client of one session presents the credential
/// from a store they share, so two exchanges overlapping in flight present the same credential
/// twice through nobody's fault. Before this, the second was read as a replay and the family
/// was revoked: the user was signed out everywhere for having a second tab open.
/// </summary>
public class IdentityServiceConcurrentRenewalTests
{
    private static IdentityService Build(out FakeRefreshTokenStore tokens,
        CapturingLogger? logger = null)
    {
        tokens = new FakeRefreshTokenStore();
        return new IdentityService(new FakeUserStore(), new FakeTokenGenerator(), tokens,
            new FakeRefreshTokenSettings(), logger ?? new CapturingLogger());
    }

    private static async Task<(IdentityService Svc, FakeRefreshTokenStore Tokens, TokenDto Session)>
        SignedIn(bool staySignedIn = false, CapturingLogger? logger = null)
    {
        var svc = Build(out var tokens, logger);
        var session = await svc.RegisterAsync(
            new RegisterDto("user@example.com", "Password123!", "User", staySignedIn));
        return (svc, tokens, session);
    }

    // ── Task 4.1 — the regression ───────────────────────────────────────────

    [Fact]
    public async Task PresentingACredentialConsumedAMomentAgo_Succeeds()
    {
        var (svc, _, session) = await SignedIn();
        await svc.RefreshAsync(session.RefreshToken);

        var result = await svc.RefreshAsync(session.RefreshToken);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PresentingACredentialConsumedAMomentAgo_DoesNotRevokeTheFamily()
    {
        var (svc, tokens, session) = await SignedIn();
        var successor = await svc.RefreshAsync(session.RefreshToken);

        await svc.RefreshAsync(session.RefreshToken);

        tokens.Tokens.Should().OnlyContain(t => t.RevokedAt == null);
        // The client that won the race keeps working — the whole point is that neither is
        // punished for the other's timing.
        var act = () => svc.RefreshAsync(successor.RefreshToken);
        await act.Should().NotThrowAsync();
    }

    // ── Task 4.4 — what the grace exchange yields ───────────────────────────

    [Fact]
    public async Task TheGraceIssuedCredential_JoinsTheSameFamily()
    {
        var (svc, tokens, session) = await SignedIn();
        var familyId = tokens.Tokens.Single().FamilyId;
        await svc.RefreshAsync(session.RefreshToken);

        await svc.RefreshAsync(session.RefreshToken);

        tokens.Tokens.Should().HaveCount(3).And.OnlyContain(t => t.FamilyId == familyId);
    }

    [Fact]
    public async Task TheGraceIssuedCredential_IsItselfExchangeable()
    {
        var (svc, _, session) = await SignedIn();
        await svc.RefreshAsync(session.RefreshToken);
        var sibling = await svc.RefreshAsync(session.RefreshToken);

        var next = await svc.RefreshAsync(sibling.RefreshToken);

        next.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheGraceIssuedCredential_CarriesTheSessionsChosenLength()
    {
        var (svc, _, session) = await SignedIn(staySignedIn: true);
        await svc.RefreshAsync(session.RefreshToken);

        var sibling = await svc.RefreshAsync(session.RefreshToken);

        sibling.RefreshTokenPersistent.Should().BeTrue();
    }

    // ── Task 4.5 — grace narrowed the replay rule, it did not remove it ─────

    [Fact]
    public async Task PresentingACredentialConsumedLongAgo_RevokesTheFamily()
    {
        var (svc, tokens, session) = await SignedIn();
        await svc.RefreshAsync(session.RefreshToken);

        // Push the consumption back beyond the window without waiting out real time.
        tokens.BackdateConsumption(TimeSpan.FromMinutes(1));

        var act = () => svc.RefreshAsync(session.RefreshToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        tokens.Tokens.Should().OnlyContain(t => t.RevokedAt != null);
    }

    [Fact]
    public async Task PresentingACredentialFromARevokedFamily_IsRefusedEvenWithinTheWindow()
    {
        var (svc, tokens, session) = await SignedIn();
        await svc.RefreshAsync(session.RefreshToken);
        await svc.LogoutAsync(session.RefreshToken);

        var act = () => svc.RefreshAsync(session.RefreshToken);

        // Signing out ended the session. Arriving inside the window must not revive it.
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _ = tokens;
    }

    // ── Task 4.6 — the window cannot be walked forward ──────────────────────

    [Fact]
    public async Task AGraceExchange_DoesNotReconsumeThePresentedCredential()
    {
        var (svc, tokens, session) = await SignedIn();
        await svc.RefreshAsync(session.RefreshToken);
        var consumedAt = tokens.Tokens[0].ConsumedAt;

        await svc.RefreshAsync(session.RefreshToken);

        // Anchored to the first exchange, so re-presenting a captured credential once a
        // second cannot keep it alive.
        tokens.Tokens[0].ConsumedAt.Should().Be(consumedAt);
    }

    // ── Task 5.1/5.3/5.4 — a grace-issued credential cannot outlive its session ──

    [Fact]
    public async Task AGraceIssuedCredential_ExpiresWithTheSessionItCameFrom()
    {
        // A remembered session that has been running a while: an hour left of its sixty days.
        var (svc, tokens, session) = await SignedIn(staySignedIn: true);
        var sessionEnd = DateTime.UtcNow.AddHours(1);
        tokens.ExpireAllAt(sessionEnd);
        await svc.RefreshAsync(session.RefreshToken);

        var sibling = await svc.RefreshAsync(session.RefreshToken);

        // Uncapped this would be sixty days out, so a credential captured and replayed inside
        // the window would buy a session far longer than the one it was taken from — and one
        // nothing the legitimate user does would end.
        sibling.RefreshTokenExpiresAt.Should().BeCloseTo(sessionEnd, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AnOrdinaryExchange_IsNotBounded()
    {
        var (svc, tokens, session) = await SignedIn(staySignedIn: true);
        tokens.ExpireAllAt(DateTime.UtcNow.AddHours(1));

        var successor = await svc.RefreshAsync(session.RefreshToken);

        // The bound is for the grace path alone. Ordinary rotation keeps sliding, which is
        // what makes continued use keep a session alive indefinitely.
        successor.RefreshTokenExpiresAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(60), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RenewingUnderGraceRepeatedly_CannotExtendTheSession()
    {
        var (svc, tokens, session) = await SignedIn(staySignedIn: true);
        var sessionEnd = DateTime.UtcNow.AddHours(1);
        tokens.ExpireAllAt(sessionEnd);
        await svc.RefreshAsync(session.RefreshToken);

        var first = await svc.RefreshAsync(session.RefreshToken);
        var second = await svc.RefreshAsync(first.RefreshToken);
        var third = await svc.RefreshAsync(first.RefreshToken);

        // The ceiling is inherited, so neither an ordinary rotation of a grace-issued
        // credential nor a further grace exchange walks the session back out. Without
        // inheritance `second` alone would restore a full sixty days.
        second.RefreshTokenExpiresAt.Should().BeCloseTo(sessionEnd, TimeSpan.FromSeconds(1));
        third.RefreshTokenExpiresAt.Should().BeCloseTo(sessionEnd, TimeSpan.FromSeconds(1));
    }

    // ── Task 6.3 — a grace exchange leaves a trace ──────────────────────────

    [Fact]
    public async Task AGraceExchange_IsLoggedWithItsFamily()
    {
        var logger = new CapturingLogger();
        var (svc, tokens, session) = await SignedIn(logger: logger);
        var familyId = tokens.Tokens.Single().FamilyId;
        await svc.RefreshAsync(session.RefreshToken);

        await svc.RefreshAsync(session.RefreshToken);

        // Grace absorbs what used to be an alarm, so a real attack — repeated grace hits on
        // one family — has to remain visible somewhere. Warning, so it clears a default level.
        logger.Entries.Should().ContainSingle()
            .Which.Should().Match<(LogLevel Level, string Message)>(e =>
                e.Level == LogLevel.Warning && e.Message.Contains(familyId.ToString()));
    }

    [Fact]
    public async Task AnOrdinaryExchange_IsNotLogged()
    {
        var logger = new CapturingLogger();
        var (svc, _, session) = await SignedIn(logger: logger);

        await svc.RefreshAsync(session.RefreshToken);

        logger.Entries.Should().BeEmpty();
    }
}

/// <summary>Records what was logged, so a test can assert the trace exists.</summary>
public class CapturingLogger : ILogger<IdentityService>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullLogger.Instance.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
