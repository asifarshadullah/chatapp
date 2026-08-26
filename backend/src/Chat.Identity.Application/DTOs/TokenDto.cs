namespace Chat.Identity.Application.DTOs;

/// <summary>
/// The result of authenticating. <paramref name="RefreshToken"/> is the raw refresh token; it
/// is carried here only so the API layer can place it in an http-only cookie, and must never
/// be serialised into a response body.
/// </summary>
public record TokenDto(string AccessToken, DateTime ExpiresAt, Guid UserId, string RefreshToken = "");
