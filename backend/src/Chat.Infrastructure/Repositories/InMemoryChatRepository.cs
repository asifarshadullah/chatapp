using System.Collections.Concurrent;
using Chat.Application.DTOs;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Application.Paging;
using Chat.Domain.Entities;
using Chat.Domain.Enums;

namespace Chat.Infrastructure.Repositories;

/// <summary>
/// Thread-safe in-memory implementation of IChatRepository.
/// Registered as Singleton so conversations persist across requests.
/// </summary>
public class InMemoryChatRepository : IChatRepository
{
    private readonly ConcurrentDictionary<Guid, Conversation> _conversations = new();

    /// <inheritdoc/>
    public Task<Conversation> CreateConversationAsync(Guid userId, string title, CancellationToken ct = default)
    {
        var conversation = new Conversation(userId, title);
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

        lock (conversation)
            conversation.AddMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetTitleAsync(Guid conversationId, Guid userId, string title, CancellationToken ct = default)
    {
        var conversation = FindOwned(conversationId, userId)
            ?? throw new ConversationNotFoundException(conversationId);

        conversation.ChangeTitle(title);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ConversationPage> ListConversationsAsync(Guid userId, int limit, string? cursor, CancellationToken ct = default)
    {
        var after = cursor is null ? (ConversationCursor?)null : ConversationCursor.Decode(cursor);

        var listed = _conversations.Values
            .Where(c => c.OwnerId == userId)
            .Select(Summarize)
            .OfType<ConversationSummary>()
            .OrderByDescending(s => s.LastActivityAt)
            .ThenByDescending(s => s.Id.ToString(), StringComparer.Ordinal)
            .Where(s => after is not { } a
                || s.LastActivityAt < a.LastActivityAt
                || (s.LastActivityAt == a.LastActivityAt && string.CompareOrdinal(s.Id.ToString(), a.Id.ToString()) < 0))
            .Take(limit + 1)
            .ToList();

        var page = listed.Take(limit).ToList();
        var next = listed.Count > limit ? new ConversationCursor(page[^1].LastActivityAt, page[^1].Id).Encode() : null;
        return Task.FromResult(new ConversationPage(page, next));
    }

    /// <summary>Null for a conversation without a reply, which is not listed.</summary>
    private static ConversationSummary? Summarize(Conversation conversation)
    {
        lock (conversation)
        {
            if (!conversation.Messages.Any(m => m.Role == MessageRole.Assistant))
                return null;
            return new ConversationSummary(conversation.Id, conversation.Title, conversation.Messages.Max(m => m.Timestamp));
        }
    }

    private Conversation? FindOwned(Guid conversationId, Guid userId) =>
        _conversations.TryGetValue(conversationId, out var conversation) && conversation.OwnerId == userId
            ? conversation
            : null;
}
