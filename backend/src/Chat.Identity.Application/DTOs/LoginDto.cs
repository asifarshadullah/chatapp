namespace Chat.Identity.Application.DTOs;

/// <summary>
/// <paramref name="StaySignedIn"/> is the user's "keep me signed in" choice. It defaults to
/// false, so a request that omits it gets the shorter session.
/// </summary>
public record LoginDto(string Email, string Password, bool StaySignedIn = false);
