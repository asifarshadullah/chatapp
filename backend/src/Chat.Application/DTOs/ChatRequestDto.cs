namespace Chat.Application.DTOs;

/// <summary>
/// Represents an incoming chat message request from the client.
/// </summary>
/// <param name="Message">The user's message content.</param>
public record ChatRequestDto(string Message);
