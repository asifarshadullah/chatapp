using Chat.Identity.Domain.Entities;

namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Persistence contract for refresh tokens. Application layer depends on this;
/// Infrastructure provides the MongoDB implementation.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Look a token up by its hash. The raw token is never stored.</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    Task UpdateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Consume a token, but only if it is still unconsumed, and report whether this caller was
    /// the one that consumed it.
    ///
    /// Two exchanges of one credential can overlap in flight — every client of a session draws
    /// the credential from the same store — and an unconditional write would let both believe
    /// they succeeded, the later silently erasing the earlier. The condition makes exactly one
    /// of them the winner; the loser is then judged on how long ago the credential was
    /// consumed, not on the order two writes happened to land in.
    /// </summary>
    Task<bool> TryConsumeAsync(RefreshToken token, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Withdraw every token in a family in one operation. Used when a consumed token is
    /// replayed, and when a user signs out.
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default);
}
