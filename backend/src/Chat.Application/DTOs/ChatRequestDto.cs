namespace Chat.Application.DTOs;

/// <summary>
/// Represents an incoming chat message request from the client.
/// </summary>
/// <param name="Message">The user's message content.</param>
/// <param name="ConversationId">Optional ID of an existing conversation to continue. Null creates a new conversation.</param>
public record ChatRequestDto(string Message, Guid? ConversationId = null);
