using Chat.Application.DTOs;
using Chat.Domain.Entities;

namespace Chat.Application.Interfaces;

/// <summary>
/// Defines the contract for conversation storage and retrieval.
/// </summary>
public interface IChatRepository
{
    /// <summary>Creates a new empty conversation owned by the given user, with a title, and persists it.</summary>
    Task<Conversation> CreateConversationAsync(Guid userId, string title, CancellationToken ct = default);

    /// <summary>Returns the conversation with the given ID, or null if not found.</summary>
    Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default);

    /// <summary>Appends a message to an existing conversation, which also marks it as active now.</summary>
    /// <exception cref="Exceptions.ConversationNotFoundException">Thrown when the conversation does not exist or is not the user's.</exception>
    Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default);

    /// <summary>Replaces the title of the user's conversation.</summary>
    /// <exception cref="Exceptions.ConversationNotFoundException">Thrown when the conversation does not exist or is not the user's.</exception>
    Task SetTitleAsync(Guid conversationId, Guid userId, string title, CancellationToken ct = default);

    /// <summary>
    /// Returns a page of the user's conversations that have a reply, most recently active first,
    /// starting after <paramref name="cursor"/> when one is given.
    /// </summary>
    /// <exception cref="Exceptions.InvalidCursorException">Thrown when the cursor was not issued by a page.</exception>
    Task<ConversationPage> ListConversationsAsync(Guid userId, int limit, string? cursor, CancellationToken ct = default);
}
