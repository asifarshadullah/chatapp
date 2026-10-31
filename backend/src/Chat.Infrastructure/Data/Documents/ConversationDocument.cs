using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Infrastructure.Data.Documents;

/// <summary>MongoDB storage representation of a conversation.</summary>
internal class ConversationDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>
    /// The user who owns the conversation. Documents written before ownership existed have no
    /// such field, so no owner filter ever matches them.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid OwnerId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Missing on documents written before titles existed, until startup backfills it.</summary>
    public string? Title { get; set; }

    /// <summary>The time of the latest message, which orders the conversation list.</summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Whether a reply has been saved. Only such conversations are listed: their id was sent to the
    /// caller with the first token, and a conversation whose reply never started was not.
    /// </summary>
    public bool HasReply { get; set; }

    public List<ChatMessageDocument> Messages { get; set; } = new();
}

/// <summary>MongoDB storage representation of a single chat message.</summary>
internal class ChatMessageDocument
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
