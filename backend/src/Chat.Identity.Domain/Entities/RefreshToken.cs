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
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// True when the user asked to stay signed in. Successors inherit it, so the choice
    /// survives rotation without being asked again.
    /// </summary>
    public bool Persistent { get; }

    /// <summary>Issue a brand-new token.</summary>
    public RefreshToken(string tokenHash, Guid userId, Guid familyId, DateTime expiresAt,
        bool persistent = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        Id = Guid.NewGuid();
        TokenHash = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        Persistent = persistent;
    }

    /// <summary>
    /// Reconstruct from storage (two-constructor pattern). Trusts the incoming values and
    /// bypasses the guards on <see cref="Consume"/>, which exist for mutation only — a token
    /// stored as already consumed must load without tripping them.
    /// </summary>
    public RefreshToken(Guid id, string tokenHash, Guid userId, Guid familyId,
        DateTime expiresAt, DateTime? consumedAt, DateTime? revokedAt, bool persistent = false)
    {
        Id = id;
        TokenHash = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
        RevokedAt = revokedAt;
        Persistent = persistent;
    }

    /// <summary>True while the token can still be exchanged.</summary>
    public bool IsUsable(DateTime now)
        => ConsumedAt is null && RevokedAt is null && now < ExpiresAt;

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
        if (now < ExpiresAt) ExpiresAt = now;
    }

    /// <summary>
    /// Withdraw the token. Idempotent, and allowed on a consumed token, because revoking a
    /// family sweeps every member regardless of the state each one is in.
    /// </summary>
    public void Revoke(DateTime now) => RevokedAt ??= now;
}
