namespace Chat.Identity.Application.DTOs;

/// <summary>
/// The result of authenticating. <paramref name="RefreshToken"/> is the raw refresh token; it
/// is carried here only so the API layer can place it in an http-only cookie, and must never
/// be serialised into a response body.
///
/// <paramref name="RefreshTokenExpiresAt"/> and <paramref name="RefreshTokenPersistent"/> come
/// along so the API layer can set the cookie from the same lifetime the credential was stored
/// with, rather than recomputing it and drifting.
/// </summary>
public record TokenDto(string AccessToken, DateTime ExpiresAt, Guid UserId,
    string RefreshToken = "", DateTime RefreshTokenExpiresAt = default,
    bool RefreshTokenPersistent = false);
