using Chat.Domain.Entities;

namespace Chat.Application.Interfaces;

/// <summary>
/// Defines the contract for conversation storage and retrieval.
/// </summary>
public interface IChatRepository
{
    /// <summary>Creates a new empty conversation owned by the given user and persists it.</summary>
    Task<Conversation> CreateConversationAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns the conversation with the given ID, or null if not found.</summary>
    Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default);

    /// <summary>Appends a message to an existing conversation.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the conversation does not exist.</exception>
    Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default);
}
