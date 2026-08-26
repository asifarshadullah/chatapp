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
    /// Withdraw every token in a family in one operation. Used when a consumed token is
    /// replayed, and when a user signs out.
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default);
}
