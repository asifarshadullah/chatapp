using Chat.Application.DTOs;

namespace Chat.Application.Interfaces;

/// <summary>
/// Defines the contract for the chat service that processes user messages.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Saves the user message, streams AI response tokens, then saves the complete assistant message.
    /// Yields (ConversationId, Token) pairs — ConversationId is populated on every item so the hub
    /// can forward it to the client on the first yield before streaming begins.
    /// </summary>
    IAsyncEnumerable<(Guid ConversationId, string Token)> StreamResponseAsync(
        string content,
        Guid userId,
        Guid? conversationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full message history for a conversation, or null if not found.
    /// </summary>
    Task<ConversationHistoryDto?> GetHistoryAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default);
}
