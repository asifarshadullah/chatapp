namespace Chat.Application.DTOs;

/// <summary>
/// Represents the full message history for a conversation.
/// </summary>
/// <param name="ConversationId">The conversation's unique identifier.</param>
/// <param name="Messages">All messages in chronological order.</param>
public record ConversationHistoryDto(Guid ConversationId, IReadOnlyList<ChatMessageDto> Messages);
