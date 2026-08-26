using Chat.Identity.Application.DTOs;

namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Application-level identity operations. Knows nothing about JWT or OAuth mechanics.
/// </summary>
public interface IIdentityService
{
    Task<TokenDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);
    Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default);
    /// <summary>
    /// Completes an external-provider sign-in. <paramref name="staySignedIn"/> is the choice
    /// the user made before being redirected to the provider, carried back through the
    /// provider round trip.
    /// </summary>
    Task<TokenDto> HandleExternalCallbackAsync(string provider, string providerKey,
        string email, string displayName, bool staySignedIn = false,
        CancellationToken ct = default);
    Task<UserProfileDto?> GetUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Exchanges a raw refresh token for a new access token and a rotated refresh token.
    /// Throws <see cref="UnauthorizedAccessException"/> when the token cannot be exchanged,
    /// without distinguishing why.
    /// </summary>
    Task<TokenDto> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);

    /// <summary>Revokes the family the given token belongs to. A no-op when it is absent.</summary>
    Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default);
}
