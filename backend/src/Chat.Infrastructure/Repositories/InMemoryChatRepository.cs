using System.Collections.Concurrent;
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
    public Task<Conversation> CreateConversationAsync(CancellationToken ct = default)
    {
        var conversation = new Conversation();
        _conversations[conversation.Id] = conversation;
        return Task.FromResult(conversation);
    }

    /// <inheritdoc/>
    public Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return Task.FromResult(conversation);
    }

    /// <inheritdoc/>
    public Task AddMessageAsync(Guid conversationId, ChatMessage message, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
            throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");

        conversation.AddMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
            throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");

        return Task.FromResult(conversation.Messages);
    }
}
