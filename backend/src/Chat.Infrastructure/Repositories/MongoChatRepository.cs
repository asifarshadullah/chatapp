using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Data.Documents;
using MongoDB.Driver;

namespace Chat.Infrastructure.Repositories;

/// <summary>
/// MongoDB-backed implementation of IChatRepository.
/// Conversations and messages are persisted across server restarts.
/// </summary>
public class MongoChatRepository : IChatRepository
{
    private readonly IMongoCollection<ConversationDocument> _conversations;

    /// <summary>
    /// Creates a new MongoChatRepository backed by the given database.
    /// </summary>
    public MongoChatRepository(IMongoDatabase database)
    {
        _conversations = database.GetCollection<ConversationDocument>("conversations");
    }

    /// <inheritdoc/>
    public async Task<Conversation> CreateConversationAsync(Guid userId, CancellationToken ct = default)
    {
        // Built through the domain first so an empty owner is refused before anything is stored.
        var conversation = new Conversation(userId);
        var doc = new ConversationDocument
        {
            Id = conversation.Id,
            OwnerId = conversation.OwnerId,
            CreatedAt = conversation.CreatedAt
        };

        await _conversations.InsertOneAsync(doc, cancellationToken: ct);
        return ToConversation(doc);
    }

    /// <inheritdoc/>
    public async Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        var doc = await _conversations
            .Find(d => d.Id == conversationId && d.OwnerId == userId)
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : ToConversation(doc);
    }

    /// <inheritdoc/>
    public async Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default)
    {
        var msgDoc = new ChatMessageDocument
        {
            Id = message.Id,
            Content = message.Content,
            Role = message.Role.ToString(),
            Timestamp = message.Timestamp
        };

        var update = Builders<ConversationDocument>.Update.Push(d => d.Messages, msgDoc);
        var result = await _conversations.UpdateOneAsync(
            d => d.Id == conversationId && d.OwnerId == userId, update, cancellationToken: ct);

        if (result.MatchedCount == 0)
            throw new ConversationNotFoundException(conversationId);
    }

    private static Conversation ToConversation(ConversationDocument doc)
    {
        var conversation = new Conversation(doc.Id, doc.OwnerId, doc.CreatedAt);
        foreach (var m in doc.Messages.OrderBy(m => m.Timestamp))
            conversation.AddMessage(ToMessage(m));
        return conversation;
    }

    private static ChatMessage ToMessage(ChatMessageDocument doc) =>
        new(doc.Id, doc.Content, Enum.Parse<MessageRole>(doc.Role), doc.Timestamp);
}
