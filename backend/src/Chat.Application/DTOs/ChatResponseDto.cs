namespace Chat.Application.DTOs;

/// <summary>
/// Represents the chat response returned to the client.
/// </summary>
/// <param name="Id">Unique identifier for this message.</param>
/// <param name="Message">The response message content.</param>
/// <param name="Role">The role of the message sender (e.g., "assistant").</param>
/// <param name="Timestamp">UTC timestamp when this message was created.</param>
/// <param name="ConversationId">The ID of the conversation this message belongs to.</param>
public record ChatResponseDto(Guid Id, string Message, string Role, DateTime Timestamp, Guid ConversationId);
