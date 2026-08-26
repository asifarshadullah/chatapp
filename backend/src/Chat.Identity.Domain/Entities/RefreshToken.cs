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
/// </summary>
public class RefreshToken
{
    public Guid Id { get; }
    public string TokenHash { get; }
    public Guid UserId { get; }
    public Guid FamilyId { get; }
    public DateTime ExpiresAt { get; }
    public DateTime? ConsumedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Issue a brand-new token.</summary>
    public RefreshToken(string tokenHash, Guid userId, Guid familyId, DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        Id = Guid.NewGuid();
        TokenHash = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Reconstruct from storage (two-constructor pattern). Trusts the incoming values and
    /// bypasses the guards on <see cref="Consume"/>, which exist for mutation only — a token
    /// stored as already consumed must load without tripping them.
    /// </summary>
    public RefreshToken(Guid id, string tokenHash, Guid userId, Guid familyId,
        DateTime expiresAt, DateTime? consumedAt, DateTime? revokedAt)
    {
        Id = id;
        TokenHash = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
        RevokedAt = revokedAt;
    }

    /// <summary>True while the token can still be exchanged.</summary>
    public bool IsUsable(DateTime now)
        => ConsumedAt is null && RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// Mark the token as spent. Throws if it was already consumed: that is a replay, and the
    /// caller must react to it rather than silently overwrite the record.
    /// </summary>
    public void Consume(DateTime now)
    {
        if (ConsumedAt is not null)
            throw new InvalidOperationException("Refresh token has already been consumed.");

        ConsumedAt = now;
    }

    /// <summary>
    /// Withdraw the token. Idempotent, and allowed on a consumed token, because revoking a
    /// family sweeps every member regardless of the state each one is in.
    /// </summary>
    public void Revoke(DateTime now) => RevokedAt ??= now;
}
