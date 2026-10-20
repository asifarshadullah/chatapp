using Chat.Application.Interfaces;
using Chat.Domain.Entities;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Wraps a repository and counts the conversations created through it, so tests can show
/// that a refused request did not quietly start a new conversation. The API has no way to
/// list conversations, so this is the only place the count is visible.
/// </summary>
public class CountingChatRepository(IChatRepository inner) : IChatRepository
{
    private int _created;

    public int ConversationsCreated => Volatile.Read(ref _created);

    public async Task<Conversation> CreateConversationAsync(Guid userId, CancellationToken ct = default)
    {
        var conversation = await inner.CreateConversationAsync(userId, ct);
        Interlocked.Increment(ref _created);
        return conversation;
    }

    public Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        inner.GetConversationAsync(conversationId, userId, ct);

    public Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default) =>
        inner.AddMessageAsync(conversationId, userId, message, ct);
}
