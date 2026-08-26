namespace Chat.Identity.Application.DTOs;

/// <summary>
/// A freshly generated refresh token: the raw value that goes to the client, and the hash
/// that is stored. The raw value is never persisted, and the hash never leaves the server.
/// </summary>
public record RefreshTokenPair(string RawToken, string TokenHash);
