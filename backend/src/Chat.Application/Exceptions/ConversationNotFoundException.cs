namespace Chat.Application.Exceptions;

/// <summary>
/// Thrown when a conversation does not exist or does not belong to the caller.
/// The two cases are deliberately indistinguishable, so a caller cannot learn whether
/// a conversation it does not own exists.
/// </summary>
public class ConversationNotFoundException : Exception
{
    /// <summary>Creates the exception for the given conversation id.</summary>
    public ConversationNotFoundException(Guid conversationId)
        : base($"Conversation '{conversationId}' not found.")
    {
        ConversationId = conversationId;
    }

    /// <summary>The id the caller asked for.</summary>
    public Guid ConversationId { get; }
}
