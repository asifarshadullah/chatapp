using Chat.Identity.Application.DTOs;

namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Application-level identity operations. Knows nothing about JWT or OAuth mechanics.
/// </summary>
public interface IIdentityService
{
    Task<TokenDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);
    Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default);
    Task<TokenDto> HandleExternalCallbackAsync(string provider, string providerKey,
        string email, string displayName, CancellationToken ct = default);
    Task<UserProfileDto?> GetUserAsync(Guid userId, CancellationToken ct = default);
}
