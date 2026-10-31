namespace Chat.Domain.Entities;

/// <summary>
/// Represents a chat conversation containing an ordered list of messages.
/// </summary>
public class Conversation
{
    private readonly List<ChatMessage> _messages = new();

    /// <summary>Unique identifier for this conversation.</summary>
    public Guid Id { get; }

    /// <summary>The user who started this conversation and the only one who may use it.</summary>
    public Guid OwnerId { get; }

    /// <summary>UTC timestamp when this conversation was created.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>A short name for the conversation, shown in the conversation list.</summary>
    public string Title { get; private set; }

    /// <summary>Ordered list of messages in this conversation.</summary>
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    private Conversation()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        Title = string.Empty;
    }

    /// <summary>
    /// Creates a new conversation owned by the given user.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when ownerId is empty.</exception>
    public Conversation(Guid ownerId, string title = "") : this()
    {
        OwnerId = RequireOwner(ownerId);
        Title = title;
    }

    /// <summary>
    /// Reconstructs a conversation from persisted storage with a known ID, owner, timestamp and title.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when ownerId is empty.</exception>
    public Conversation(Guid id, Guid ownerId, DateTime createdAt, string title = "") : this(id, createdAt)
    {
        OwnerId = RequireOwner(ownerId);
        Title = title;
    }

    private Conversation(Guid id, DateTime createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
        Title = string.Empty;
    }

    private static Guid RequireOwner(Guid ownerId) =>
        ownerId == Guid.Empty
            ? throw new ArgumentException("A conversation must have an owner.", nameof(ownerId))
            : ownerId;

    /// <summary>Replaces the title, as a generated title replaces the first-message one.</summary>
    public void ChangeTitle(string title) => Title = title;

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
