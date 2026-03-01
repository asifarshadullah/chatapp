using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;

namespace Chat.Application.Services;

/// <summary>
/// Processes chat messages, maintains conversation history, and returns echo responses.
/// </summary>
public class ChatService : IChatService
{
    private readonly IChatRepository _repository;

    /// <summary>
    /// Initializes a new instance of <see cref="ChatService"/>.
    /// </summary>
    /// <param name="repository">Repository for conversation storage.</param>
    public ChatService(IChatRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<ChatResponseDto> SendMessageAsync(
        string content,
        Guid? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        var userMessage = new ChatMessage(content, MessageRole.User);

        var conversation = conversationId.HasValue
            ? await _repository.GetConversationAsync(conversationId.Value, cancellationToken)
              ?? throw new KeyNotFoundException($"Conversation '{conversationId}' not found.")
            : await _repository.CreateConversationAsync(cancellationToken);

        await _repository.AddMessageAsync(conversation.Id, userMessage, cancellationToken);

        var echoMessage = new ChatMessage($"Echo: {userMessage.Content}", MessageRole.Assistant);
        await _repository.AddMessageAsync(conversation.Id, echoMessage, cancellationToken);

        return new ChatResponseDto(
            echoMessage.Id,
            echoMessage.Content,
            echoMessage.Role.ToString().ToLowerInvariant(),
            echoMessage.Timestamp,
            conversation.Id);
    }

    /// <inheritdoc/>
    public async Task<ConversationHistoryDto?> GetHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null)
            return null;

        var messages = conversation.Messages
            .Select(m => new ChatMessageDto(
                m.Id,
                m.Content,
                m.Role.ToString().ToLowerInvariant(),
                m.Timestamp))
            .ToList();

        return new ConversationHistoryDto(conversationId, messages);
    }
}
