using Chat.Domain.Entities;

namespace Chat.Application.Interfaces;

/// <summary>
/// Defines the contract for conversation storage and retrieval.
/// </summary>
public interface IChatRepository
{
    /// <summary>Creates a new empty conversation and persists it.</summary>
    Task<Conversation> CreateConversationAsync(CancellationToken ct = default);

    /// <summary>Returns the conversation with the given ID, or null if not found.</summary>
    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Appends a message to an existing conversation.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the conversation does not exist.</exception>
    Task AddMessageAsync(Guid conversationId, ChatMessage message, CancellationToken ct = default);

    /// <summary>Returns all messages for a conversation in chronological order.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the conversation does not exist.</exception>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default);
}
