using Chat.Application.DTOs;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Application.Paging;
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
    private const string CollectionName = "conversations";

    private readonly IMongoCollection<ConversationDocument> _conversations;

    /// <summary>
    /// Creates a new MongoChatRepository backed by the given database.
    /// </summary>
    public MongoChatRepository(IMongoDatabase database)
    {
        _conversations = database.GetCollection<ConversationDocument>(CollectionName);
    }

    /// <inheritdoc/>
    public async Task<Conversation> CreateConversationAsync(Guid userId, string title, CancellationToken ct = default)
    {
        // Built through the domain first so an empty owner is refused before anything is stored.
        var conversation = new Conversation(userId, title);
        var doc = new ConversationDocument
        {
            Id = conversation.Id,
            OwnerId = conversation.OwnerId,
            CreatedAt = conversation.CreatedAt,
            Title = conversation.Title,
            LastActivityAt = conversation.CreatedAt,
            HasReply = false
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

        var update = Builders<ConversationDocument>.Update
            .Push(d => d.Messages, msgDoc)
            // Saves from two tabs can land out of order; activity must not move back.
            .Max(d => d.LastActivityAt, message.Timestamp);
        if (message.Role == MessageRole.Assistant)
            update = update.Set(d => d.HasReply, true);

        var result = await _conversations.UpdateOneAsync(
            d => d.Id == conversationId && d.OwnerId == userId, update, cancellationToken: ct);

        if (result.MatchedCount == 0)
            throw new ConversationNotFoundException(conversationId);
    }

    /// <inheritdoc/>
    public async Task SetTitleAsync(Guid conversationId, Guid userId, string title, CancellationToken ct = default)
    {
        var result = await _conversations.UpdateOneAsync(
            d => d.Id == conversationId && d.OwnerId == userId,
            Builders<ConversationDocument>.Update.Set(d => d.Title, title),
            cancellationToken: ct);

        if (result.MatchedCount == 0)
            throw new ConversationNotFoundException(conversationId);
    }

    /// <inheritdoc/>
    public async Task<ConversationPage> ListConversationsAsync(Guid userId, int limit, string? cursor, CancellationToken ct = default)
    {
        var filter = Builders<ConversationDocument>.Filter;
        var listed = filter.Eq(d => d.OwnerId, userId) & filter.Eq(d => d.HasReply, true);
        if (cursor is not null)
        {
            var after = ConversationCursor.Decode(cursor);
            listed &= filter.Lt(d => d.LastActivityAt, after.LastActivityAt)
                | (filter.Eq(d => d.LastActivityAt, after.LastActivityAt) & filter.Lt(d => d.Id, after.Id));
        }

        // One extra document says whether another page follows.
        var docs = await _conversations.Find(listed)
            .Sort(Builders<ConversationDocument>.Sort.Descending(d => d.LastActivityAt).Descending(d => d.Id))
            .Limit(limit + 1)
            .Project(d => new { d.Id, d.Title, d.LastActivityAt })
            .ToListAsync(ct);

        var page = docs.Take(limit)
            .Select(d => new ConversationSummary(d.Id, d.Title ?? string.Empty, DateTime.SpecifyKind(d.LastActivityAt, DateTimeKind.Utc)))
            .ToList();
        var next = docs.Count > limit ? new ConversationCursor(page[^1].LastActivityAt, page[^1].Id).Encode() : null;
        return new ConversationPage(page, next);
    }

    /// <summary>
    /// Creates the conversation list's index and gives conversations saved before titles existed a
    /// title, a last activity time and a reply flag. Safe to run on every start.
    /// </summary>
    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct = default)
    {
        var conversations = database.GetCollection<ConversationDocument>(CollectionName);

        await conversations.Indexes.CreateOneAsync(
            new CreateIndexModel<ConversationDocument>(
                Builders<ConversationDocument>.IndexKeys
                    .Ascending(d => d.OwnerId).Descending(d => d.LastActivityAt).Descending(d => d.Id),
                new CreateIndexOptions<ConversationDocument>
                {
                    Name = "owner_lastActivity_listed",
                    PartialFilterExpression = Builders<ConversationDocument>.Filter.Eq(d => d.HasReply, true)
                }),
            cancellationToken: ct);

        // Typed reads cannot tell a missing field from its default, so the filter names the fields.
        var filter = Builders<ConversationDocument>.Filter;
        var outdated = filter.Exists(d => d.OwnerId)
            & (filter.Exists(d => d.Title, false) | filter.Exists(d => d.LastActivityAt, false) | filter.Exists(d => d.HasReply, false));

        using var cursor = await conversations.Find(outdated).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var doc in cursor.Current)
            {
                var messages = doc.Messages.OrderBy(m => m.Timestamp).ToList();
                var update = Builders<ConversationDocument>.Update
                    .Set(d => d.Title, messages.Count > 0 ? ConversationTitle.From(messages[0].Content) : UntitledTitle)
                    .Set(d => d.LastActivityAt, messages.Count > 0 ? messages[^1].Timestamp : doc.CreatedAt)
                    .Set(d => d.HasReply, messages.Any(m => m.Role == nameof(MessageRole.Assistant)));
                await conversations.UpdateOneAsync(d => d.Id == doc.Id, update, cancellationToken: ct);
            }
        }
    }

    private const string UntitledTitle = "New conversation";

    private static Conversation ToConversation(ConversationDocument doc)
    {
        var conversation = new Conversation(doc.Id, doc.OwnerId, doc.CreatedAt, doc.Title ?? string.Empty);
        foreach (var m in doc.Messages.OrderBy(m => m.Timestamp))
            conversation.AddMessage(ToMessage(m));
        return conversation;
    }

    private static ChatMessage ToMessage(ChatMessageDocument doc) =>
        new(doc.Id, doc.Content, Enum.Parse<MessageRole>(doc.Role), doc.Timestamp);
}
