namespace Chat.Identity.Domain.Entities;

/// <summary>
/// A revocable credential that can be exchanged for a fresh access token.
/// Lives in the Chat.Identity bounded context.
///
/// Only a hash of the token is held here — the raw value goes to the client and is never
/// stored, so a leaked database yields no usable credentials.
///
/// Tokens issued from one authentication share a <see cref="FamilyId"/>. A legitimate client
/// discards each token as it is consumed, so a consumed token presented again means it was
/// captured and replayed; the caller revokes the whole family in response.
///
/// <see cref="Persistent"/> records the length of session the user asked for. It rides on the
/// credential rather than on the user because it describes one device's session: opting in on
/// a phone must not lengthen the same user's session on a shared desktop.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; }
    public string TokenHash { get; }
    public Guid UserId { get; }
    public Guid FamilyId { get; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }

    /// <summary>
    /// The expiry this credential held before it was consumed, or null while it is unconsumed.
    ///
    /// <see cref="Consume"/> pulls <see cref="ExpiresAt"/> in to the moment of consumption so
    /// the store can reap the record promptly, which erases the only record of when the
    /// session itself would have run out. A credential issued because an exchange arrived
    /// within the grace window is bounded by that moment — otherwise a replayed credential
    /// would buy a full-length session, unbounded by the one it came from.
    /// </summary>
    public DateTime? PreConsumptionExpiresAt { get; private set; }

    /// <summary>
    /// A ceiling this credential may not be exchangeable beyond, or null when the session has
    /// never been renewed under the grace window.
    ///
    /// Set when a credential is issued because an exchange arrived within the grace window,
    /// and inherited by every successor thereafter — ordinary rotations included. Inheritance
    /// is the whole point: a grace-issued credential is otherwise unremarkable, so without it
    /// one ordinary rotation would slide the session back out to a full lifetime and a
    /// replayed credential would escape the bound for the price of one more request.
    ///
    /// A session that never renews under grace never acquires a ceiling, so ordinary sliding
    /// renewal is untouched and continued use still keeps a session alive indefinitely.
    /// </summary>
    public DateTime? SessionExpiresAt { get; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// True when the user asked to stay signed in. Successors inherit it, so the choice
    /// survives rotation without being asked again.
    /// </summary>
    public bool Persistent { get; }

    /// <summary>Issue a brand-new token.</summary>
    public RefreshToken(string tokenHash, Guid userId, Guid familyId, DateTime expiresAt,
        bool persistent = false, DateTime? sessionExpiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        Id = Guid.NewGuid();
        TokenHash = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        Persistent = persistent;
        SessionExpiresAt = sessionExpiresAt;
    }

    /// <summary>
    /// Reconstruct from storage (two-constructor pattern). Trusts the incoming values and
    /// bypasses the guards on <see cref="Consume"/>, which exist for mutation only — a token
    /// stored as already consumed must load without tripping them.
    /// </summary>
    public RefreshToken(Guid id, string tokenHash, Guid userId, Guid familyId,
        DateTime expiresAt, DateTime? consumedAt, DateTime? revokedAt, bool persistent = false,
        DateTime? preConsumptionExpiresAt = null, DateTime? sessionExpiresAt = null)
    {
        Id = id;
        TokenHash = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
        RevokedAt = revokedAt;
        Persistent = persistent;
        PreConsumptionExpiresAt = preConsumptionExpiresAt;
        SessionExpiresAt = sessionExpiresAt;
    }

    /// <summary>True while the token can still be exchanged.</summary>
    public bool IsUsable(DateTime now)
        => ConsumedAt is null && RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// Whether this credential was consumed recently enough that the caller presenting it now
    /// is more likely the legitimate holder renewing concurrently than an attacker replaying
    /// a capture.
    ///
    /// Every client of one session draws its credential from the same store, so two exchanges
    /// overlapping in flight present the same credential twice through nobody's fault. The
    /// window is anchored to <see cref="ConsumedAt"/> and never moves, so re-presenting cannot
    /// extend it. A revoked credential is excluded: a family ended by a real replay or by
    /// signing out must not be revived by a renewal that happens to arrive in time.
    ///
    /// Deliberately silent about <see cref="ExpiresAt"/>, which <see cref="Consume"/> has
    /// already pulled in to the moment of consumption — testing it here would reject every
    /// caller. The session's own lifetime is enforced by the credential having been usable
    /// when it was consumed, and by the bound on what is issued in response.
    ///
    /// The duration is policy and is passed in rather than known here.
    /// </summary>
    public bool IsWithinGrace(DateTime now, TimeSpan grace)
        => ConsumedAt is { } consumedAt
           && RevokedAt is null
           && now - consumedAt <= grace;

    /// <summary>
    /// Mark the token as spent. Throws if it was already consumed: that is a replay, and the
    /// caller must react to it rather than silently overwrite the record.
    ///
    /// Consuming also pulls <see cref="ExpiresAt"/> in to now. The token is dead from this
    /// moment, and the only reason to keep the record at all is so that a replay of it is
    /// recognised as a replay rather than as an unknown credential — a job for the store's
    /// retention margin, not for the remainder of a session that may have had a month left
    /// on it. Never pushed outwards: a token consumed after it had already lapsed keeps the
    /// earlier expiry.
    /// </summary>
    public void Consume(DateTime now)
    {
        if (ConsumedAt is not null)
            throw new InvalidOperationException("Refresh token has already been consumed.");

        ConsumedAt = now;
        PreConsumptionExpiresAt = ExpiresAt;
        if (now < ExpiresAt) ExpiresAt = now;
    }

    /// <summary>
    /// Withdraw the token. Idempotent, and allowed on a consumed token, because revoking a
    /// family sweeps every member regardless of the state each one is in.
    /// </summary>
    public void Revoke(DateTime now) => RevokedAt ??= now;
}
