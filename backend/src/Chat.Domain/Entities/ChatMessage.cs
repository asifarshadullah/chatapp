using Chat.Domain.Enums;

namespace Chat.Domain.Entities;

/// <summary>
/// Represents a single message in a chat conversation.
/// </summary>
public class ChatMessage
{
    /// <summary>Maximum allowed length for message content.</summary>
    public const int MaxContentLength = 5000;

    /// <summary>Unique identifier for this message.</summary>
    public Guid Id { get; }

    /// <summary>The text content of the message.</summary>
    public string Content { get; }

    /// <summary>Whether this message is from the user or the assistant.</summary>
    public MessageRole Role { get; }

    /// <summary>UTC timestamp when this message was created.</summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Creates a new chat message with validation.
    /// </summary>
    /// <param name="content">The message text. Must not be null, empty, or exceed 5000 characters.</param>
    /// <param name="role">The role of the message sender.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when content is null, empty, whitespace, or exceeds <see cref="MaxContentLength"/> characters.
    /// </exception>
    public ChatMessage(string content, MessageRole role)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be null, empty, or whitespace.", nameof(content));
        }

        if (content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"Content cannot exceed {MaxContentLength} characters. Received {content.Length}.",
                nameof(content));
        }

        Id = Guid.NewGuid();
        Content = content;
        Role = role;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Reconstructs a chat message from persisted storage with known identity fields.
    /// </summary>
    public ChatMessage(Guid id, string content, MessageRole role, DateTime timestamp)
    {
        Id = id;
        Content = content;
        Role = role;
        Timestamp = timestamp;
    }
}
