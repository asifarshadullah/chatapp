using Chat.Identity.Domain.Entities;
using FluentAssertions;

namespace Chat.Identity.Tests.Domain;

/// <summary>
/// Unit tests for the RefreshToken entity. Usability, consumption and revocation are
/// domain rules with no I/O, so they are tested here rather than through a store.
/// </summary>
public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshToken Issue(DateTime? expiresAt = null, Guid? familyId = null)
        => new("hash-of-raw-token", Guid.NewGuid(), familyId ?? Guid.NewGuid(),
            expiresAt ?? Now.AddDays(14));

    // ── Task 1.1 — usability ─────────────────────────────────────────────────

    [Fact]
    public void NewToken_IsUsable()
    {
        var token = Issue();

        token.IsUsable(Now).Should().BeTrue();
    }

    [Fact]
    public void NewToken_CarriesTheValuesItWasIssuedWith()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        var token = new RefreshToken("hash", userId, familyId, Now.AddDays(14));

        token.Id.Should().NotBeEmpty();
        token.TokenHash.Should().Be("hash");
        token.UserId.Should().Be(userId);
        token.FamilyId.Should().Be(familyId);
        token.ExpiresAt.Should().Be(Now.AddDays(14));
        token.ConsumedAt.Should().BeNull();
        token.RevokedAt.Should().BeNull();
    }

    // ── Task 2.1 — the session's chosen length rides on the credential ───────

    [Fact]
    public void NewToken_IsNotPersistentUnlessAskedFor()
    {
        var token = Issue();

        token.Persistent.Should().BeFalse();
    }

    [Fact]
    public void NewToken_RemembersThatItIsPersistent()
    {
        var token = new RefreshToken("hash", Guid.NewGuid(), Guid.NewGuid(),
            Now.AddDays(30), persistent: true);

        token.Persistent.Should().BeTrue();
    }

    [Fact]
    public void ReconstructedToken_KeepsItsPersistence()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", Guid.NewGuid(), Guid.NewGuid(),
            Now.AddDays(30), consumedAt: null, revokedAt: null, persistent: true);

        token.Persistent.Should().BeTrue();
    }

    [Fact]
    public void ExpiredToken_IsNotUsable()
    {
        var token = Issue(expiresAt: Now.AddSeconds(-1));

        token.IsUsable(Now).Should().BeFalse();
    }

    [Fact]
    public void ConsumedToken_IsNotUsable()
    {
        var token = Issue();
        token.Consume(Now);

        token.IsUsable(Now).Should().BeFalse();
    }

    [Fact]
    public void RevokedToken_IsNotUsable()
    {
        var token = Issue();
        token.Revoke(Now);

        token.IsUsable(Now).Should().BeFalse();
    }

    // ── Task 1.2 — consumption and revocation rules ──────────────────────────

    [Fact]
    public void Consume_RecordsWhenItHappened()
    {
        var token = Issue();

        token.Consume(Now);

        token.ConsumedAt.Should().Be(Now);
    }

    [Fact]
    public void Consume_OnAnAlreadyConsumedToken_IsRejected()
    {
        var token = Issue();
        token.Consume(Now);

        // Reaching here twice means a replay, which the caller must detect rather
        // than quietly overwrite.
        var act = () => token.Consume(Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Revoke_IsIdempotent()
    {
        var token = Issue();

        token.Revoke(Now);
        token.Revoke(Now.AddMinutes(5));

        // Revoking a whole family will re-revoke members that are already revoked,
        // so this must not throw, and must keep the first revocation time.
        token.RevokedAt.Should().Be(Now);
    }

    [Fact]
    public void Revoke_OnAConsumedToken_IsAllowed()
    {
        var token = Issue();
        token.Consume(Now);

        var act = () => token.Revoke(Now.AddMinutes(1));

        // Reuse detection revokes every member of the family, consumed ones included.
        act.Should().NotThrow();
        token.RevokedAt.Should().Be(Now.AddMinutes(1));
    }

    // ── Task 1.3 — reconstruction from storage ───────────────────────────────

    [Fact]
    public void Reconstruct_ConsumedAndExpiredToken_DoesNotTripItsOwnGuards()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var consumedAt = Now.AddDays(-2);
        var revokedAt = Now.AddDays(-1);

        // The mutation guard on Consume() must not block loading a token that was
        // already consumed before it was stored — the trap recorded in
        // docs/architecture/scenarios/01-archive-conversation/retrospective.md.
        var act = () => new RefreshToken(id, "hash", userId, familyId,
            expiresAt: Now.AddDays(-3), consumedAt: consumedAt, revokedAt: revokedAt);

        act.Should().NotThrow();

        var token = act();
        token.Id.Should().Be(id);
        token.ConsumedAt.Should().Be(consumedAt);
        token.RevokedAt.Should().Be(revokedAt);
        token.IsUsable(Now).Should().BeFalse();
    }

    [Fact]
    public void Reconstruct_UnusedToken_RoundTripsAsUsable()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", Guid.NewGuid(), Guid.NewGuid(),
            expiresAt: Now.AddDays(14), consumedAt: null, revokedAt: null);

        token.IsUsable(Now).Should().BeTrue();
    }
}
