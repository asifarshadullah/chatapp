namespace Chat.Api.Contracts;

/// <param name="Message">Nullable so a missing message reaches our "Message cannot be empty." check.</param>
/// <param name="ConversationId">The conversation to continue, or null to start a new one.</param>
/// <param name="Model">The model option to answer with, or null for the deployment's default.</param>
public record SendMessageRequest(string? Message, Guid? ConversationId, string? Model = null);
