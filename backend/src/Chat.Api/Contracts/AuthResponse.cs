using Chat.Identity.Application.DTOs;

namespace Chat.Api.Contracts;

/// <summary>
/// What an authentication endpoint returns in its body. Deliberately a separate type from
/// <see cref="TokenDto"/>: the refresh token travels only in an http-only cookie, and giving
/// the response its own shape is what makes "never in a body" enforceable in one place
/// rather than depending on every call site remembering to strip it.
/// </summary>
public record AuthResponse(string AccessToken, DateTime ExpiresAt, Guid UserId)
{
    public static AuthResponse From(TokenDto dto) => new(dto.AccessToken, dto.ExpiresAt, dto.UserId);
}
