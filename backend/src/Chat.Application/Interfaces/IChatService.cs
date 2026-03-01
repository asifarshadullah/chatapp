using Chat.Application.DTOs;

namespace Chat.Application.Interfaces;

/// <summary>
/// Defines the contract for the chat service that processes user messages.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Processes a user message, stores both messages in the conversation, and returns an echo response.
    /// Creates a new conversation when <paramref name="conversationId"/> is null.
    /// </summary>
    /// <param name="content">The user's message content.</param>
    /// <param name="conversationId">Optional existing conversation to continue. Null creates a new one.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ChatResponseDto"/> including the ConversationId.</returns>
    /// <exception cref="ArgumentException">Thrown when content is null, empty, or whitespace.</exception>
    Task<ChatResponseDto> SendMessageAsync(string content, Guid? conversationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full message history for a conversation, or null if not found.
    /// </summary>
    Task<ConversationHistoryDto?> GetHistoryAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
