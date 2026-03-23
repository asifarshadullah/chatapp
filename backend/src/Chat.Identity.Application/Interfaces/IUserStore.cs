using Chat.Identity.Domain.Entities;

namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Minimal persistence contract for AppUser. Application layer depends on this;
/// Infrastructure provides the MongoDB implementation.
/// </summary>
public interface IUserStore
{
    Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<AppUser?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppUser?> FindByLoginAsync(string provider, string providerKey, CancellationToken ct = default);
    Task CreateAsync(AppUser user, CancellationToken ct = default);
    Task UpdateAsync(AppUser user, CancellationToken ct = default);
}
