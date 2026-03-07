namespace Chat.Domain.Entities;

/// <summary>
/// Represents a chat conversation containing an ordered list of messages.
/// </summary>
public class Conversation
{
    private readonly List<ChatMessage> _messages = new();

    /// <summary>Unique identifier for this conversation.</summary>
    public Guid Id { get; }

    /// <summary>UTC timestamp when this conversation was created.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Ordered list of messages in this conversation.</summary>
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    /// <summary>
    /// Creates a new conversation with a unique ID and empty message list.
    /// </summary>
    public Conversation()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reconstructs a conversation from persisted storage with a known ID and timestamp.
    /// </summary>
    public Conversation(Guid id, DateTime createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Appends a message to this conversation.
    /// </summary>
    /// <param name="message">The message to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    public void AddMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
    }
}
