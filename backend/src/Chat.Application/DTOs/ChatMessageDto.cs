namespace Chat.Application.DTOs;

/// <summary>
/// Represents a single message within a conversation history response.
/// </summary>
/// <param name="Id">Unique identifier for this message.</param>
/// <param name="Content">The text content of the message.</param>
/// <param name="Role">The role of the sender (e.g., "user" or "assistant").</param>
/// <param name="Timestamp">UTC timestamp when this message was created.</param>
public record ChatMessageDto(Guid Id, string Content, string Role, DateTime Timestamp);
