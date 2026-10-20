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
