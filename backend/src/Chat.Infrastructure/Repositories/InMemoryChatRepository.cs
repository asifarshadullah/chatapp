using System.Collections.Concurrent;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;

namespace Chat.Infrastructure.Repositories;

/// <summary>
/// Thread-safe in-memory implementation of IChatRepository.
/// Registered as Singleton so conversations persist across requests.
/// </summary>
public class InMemoryChatRepository : IChatRepository
{
    private readonly ConcurrentDictionary<Guid, Conversation> _conversations = new();

    /// <inheritdoc/>
    public Task<Conversation> CreateConversationAsync(Guid userId, CancellationToken ct = default)
    {
        var conversation = new Conversation(userId);
        _conversations[conversation.Id] = conversation;
        return Task.FromResult(conversation);
    }

    /// <inheritdoc/>
    public Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(FindOwned(conversationId, userId));

    /// <inheritdoc/>
    public Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default)
    {
        var conversation = FindOwned(conversationId, userId)
            ?? throw new ConversationNotFoundException(conversationId);

        conversation.AddMessage(message);
        return Task.CompletedTask;
    }

    private Conversation? FindOwned(Guid conversationId, Guid userId) =>
        _conversations.TryGetValue(conversationId, out var conversation) && conversation.OwnerId == userId
            ? conversation
            : null;
}
