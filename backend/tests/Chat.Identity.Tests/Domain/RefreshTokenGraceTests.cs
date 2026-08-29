using Chat.Identity.Domain.Entities;
using FluentAssertions;

namespace Chat.Identity.Tests.Domain;

/// <summary>
/// Group 2 — the grace window. Every client of one session draws its credential from the same
/// store, so two exchanges that overlap in flight necessarily present the same credential
/// twice. Whether that is the legitimate holder renewing or an attacker replaying is decided
/// by one thing only: how long ago the credential was consumed.
/// </summary>
public class RefreshTokenGraceTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private static RefreshToken Issue()
        => new("hash-of-raw-token", Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(30));

    // ── Task 2.1 ────────────────────────────────────────────────────────────

    [Fact]
    public void AJustConsumedToken_IsWithinGrace()
    {
        var token = Issue();
        token.Consume(Now);

        token.IsWithinGrace(Now.AddSeconds(1), Grace).Should().BeTrue();
    }

    [Fact]
    public void ALongConsumedToken_IsNotWithinGrace()
    {
        var token = Issue();
        token.Consume(Now);

        token.IsWithinGrace(Now.AddMinutes(1), Grace).Should().BeFalse();
    }

    [Fact]
    public void AtTheExactEdge_IsStillWithinGrace()
    {
        var token = Issue();
        token.Consume(Now);

        // Inclusive, so a deployment setting the window to N seconds gets N, not N minus a tick.
        token.IsWithinGrace(Now.Add(Grace), Grace).Should().BeTrue();
        token.IsWithinGrace(Now.Add(Grace).AddTicks(1), Grace).Should().BeFalse();
    }

    // ── Task 2.3 ────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnconsumedToken_IsNeverWithinGrace()
    {
        var token = Issue();

        // Grace answers "was this consumed a moment ago", not "is this usable". An unconsumed
        // credential is handled by the ordinary path and must never reach here.
        token.IsWithinGrace(Now, Grace).Should().BeFalse();
    }

    [Fact]
    public void ARevokedToken_IsNotWithinGrace_HoweverRecentlyConsumed()
    {
        var token = Issue();
        token.Consume(Now);
        token.Revoke(Now);

        // A family revoked by a real replay, or by signing out, must not be resurrected by a
        // renewal that happens to arrive within the window.
        token.IsWithinGrace(Now.AddMilliseconds(1), Grace).Should().BeFalse();
    }

    // ── Task 2.4 ────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindowIsAnchoredToConsumption_NotToBeingAsked()
    {
        var token = Issue();
        token.Consume(Now);

        // Asking repeatedly must not move the window: otherwise an attacker re-presenting a
        // captured credential once a second would keep it alive indefinitely.
        for (var second = 1; second <= 30; second++)
            token.IsWithinGrace(Now.AddSeconds(second), Grace);

        token.IsWithinGrace(Now.AddSeconds(60), Grace).Should().BeFalse();
        token.ConsumedAt.Should().Be(Now);
    }
}
