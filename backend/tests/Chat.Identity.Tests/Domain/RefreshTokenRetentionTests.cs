using Chat.Identity.Domain.Entities;
using FluentAssertions;

namespace Chat.Identity.Tests.Domain;

/// <summary>
/// Task 7.3 — a consumed credential is dead the moment it is consumed, so the store has no
/// reason to keep it until the session it belonged to would have run out. With sliding
/// 30-day lifetimes that difference is the whole point: retaining every spent credential for
/// a month makes storage grow with how long sessions last rather than with how many there
/// are. What is still needed is a window long enough for a replay to be recognised.
/// </summary>
public class RefreshTokenRetentionTests
{
    private static readonly DateTime Now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken Remembered()
        => new("hash-of-raw-token", Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(30), true);

    [Fact]
    public void Consuming_PullsTheExpiryInFromTheSessionsLifetime()
    {
        var token = Remembered();

        token.Consume(Now);

        token.ExpiresAt.Should().Be(Now);
    }

    [Fact]
    public void Consuming_DoesNotPushAnAlreadyEarlierExpiryOut()
    {
        // A credential consumed after it had already lapsed must not gain retention by it.
        var token = new RefreshToken("hash", Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1));

        token.Consume(Now);

        token.ExpiresAt.Should().Be(Now.AddDays(-1));
    }

    [Fact]
    public void AConsumedTokenIsStillNotUsable()
    {
        var token = Remembered();

        token.Consume(Now);

        token.IsUsable(Now).Should().BeFalse();
    }

    [Fact]
    public void AConsumedTokenIsStillRecognisableAsAReplay()
    {
        var token = Remembered();
        token.Consume(Now);

        // Replay is detected from ConsumedAt, not from the expiry — pulling the expiry in
        // must not turn a replay into a merely-unknown credential.
        token.ConsumedAt.Should().NotBeNull();
    }

    [Fact]
    public void AnUnconsumedTokenKeepsItsFullLifetime()
    {
        var token = Remembered();

        token.ExpiresAt.Should().Be(Now.AddDays(30));
    }
}
