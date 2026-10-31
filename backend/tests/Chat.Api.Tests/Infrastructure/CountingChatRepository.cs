using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Wraps a repository and counts the conversations created through it, so tests can show
/// that a refused request did not quietly start a new conversation. The API lists only
/// conversations with a reply, so this is the only place the full count is visible.
/// </summary>
public class CountingChatRepository(IChatRepository inner) : IChatRepository
{
    private int _created;
    private int _repliesAdded;

    public int ConversationsCreated => Volatile.Read(ref _created);

    /// <summary>For tests where the caller never learns the conversation id.</summary>
    public int RepliesAdded => Volatile.Read(ref _repliesAdded);

    public async Task<Conversation> CreateConversationAsync(Guid userId, string title, CancellationToken ct = default)
    {
        var conversation = await inner.CreateConversationAsync(userId, title, ct);
        Interlocked.Increment(ref _created);
        return conversation;
    }

    public Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        inner.GetConversationAsync(conversationId, userId, ct);

    public Task SetTitleAsync(Guid conversationId, Guid userId, string title, CancellationToken ct = default) =>
        inner.SetTitleAsync(conversationId, userId, title, ct);

    public Task<ConversationPage> ListConversationsAsync(Guid userId, int limit, string? cursor, CancellationToken ct = default) =>
        inner.ListConversationsAsync(userId, limit, cursor, ct);

    // Like MongoDB, a cancelled token refuses the write.
    public async Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await inner.AddMessageAsync(conversationId, userId, message, ct);
        if (message.Role == MessageRole.Assistant)
            Interlocked.Increment(ref _repliesAdded);
    }
}
