using System.Runtime.CompilerServices;
using System.Text;
using Chat.Application.DTOs;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;

namespace Chat.Application.Services;

/// <summary>
/// Processes chat messages using an AI provider, maintains conversation history.
/// </summary>
public class ChatService : IChatService
{
    private readonly IChatRepository _repository;
    private readonly IAiProvider _aiProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="ChatService"/>.
    /// </summary>
    public ChatService(IChatRepository repository, IAiProvider aiProvider)
    {
        _repository = repository;
        _aiProvider = aiProvider;
    }

    /// <inheritdoc/>
    public async Task<ChatResponseDto> SendMessageAsync(
        string content,
        Guid userId,
        Guid? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        var userMessage = new ChatMessage(content, MessageRole.User);
        RequireUser(userId);

        var conversation = conversationId.HasValue
            ? await _repository.GetConversationAsync(conversationId.Value, userId, cancellationToken)
              ?? throw new ConversationNotFoundException(conversationId.Value)
            : await _repository.CreateConversationAsync(userId, cancellationToken);

        await _repository.AddMessageAsync(conversation.Id, userId, userMessage, cancellationToken);

        // Reload conversation so the AI provider receives the full history including the new user message
        var updated = await _repository.GetConversationAsync(conversation.Id, userId, cancellationToken)
            ?? throw new InvalidOperationException("Conversation disappeared after insert.");

        var sb = new StringBuilder();
        await foreach (var token in _aiProvider.StreamCompletionAsync(updated.Messages, cancellationToken))
            sb.Append(token);

        var assistantMessage = new ChatMessage(sb.ToString(), MessageRole.Assistant);
        await _repository.AddMessageAsync(conversation.Id, userId, assistantMessage, cancellationToken);

        return new ChatResponseDto(
            assistantMessage.Id,
            assistantMessage.Content,
            assistantMessage.Role.ToString().ToLowerInvariant(),
            assistantMessage.Timestamp,
            conversation.Id);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(Guid ConversationId, string Token)> StreamResponseAsync(
        string content,
        Guid userId,
        Guid? conversationId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var userMessage = new ChatMessage(content, MessageRole.User);
        RequireUser(userId);

        var conversation = conversationId.HasValue
            ? await _repository.GetConversationAsync(conversationId.Value, userId, ct)
              ?? throw new ConversationNotFoundException(conversationId.Value)
            : await _repository.CreateConversationAsync(userId, ct);

        await _repository.AddMessageAsync(conversation.Id, userId, userMessage, ct);

        // Reload so AI receives full history including the just-saved user message
        var updated = await _repository.GetConversationAsync(conversation.Id, userId, ct)
            ?? throw new InvalidOperationException("Conversation disappeared after insert.");

        var sb = new StringBuilder();
        await foreach (var token in _aiProvider.StreamCompletionAsync(updated.Messages, ct))
        {
            sb.Append(token);
            yield return (conversation.Id, token);
        }

        if (sb.Length > 0)
        {
            var assistantMessage = new ChatMessage(sb.ToString(), MessageRole.Assistant);
            await _repository.AddMessageAsync(conversation.Id, userId, assistantMessage, ct);
        }
    }

    /// <inheritdoc/>
    public async Task<ConversationHistoryDto?> GetHistoryAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        RequireUser(userId);
        var conversation = await _repository.GetConversationAsync(conversationId, userId, cancellationToken);
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

    // An empty id would match no conversation and quietly look like "not found"; failing
    // loudly exposes a caller that never resolved who is asking.
    private static void RequireUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A user id is required.", nameof(userId));
    }
}
