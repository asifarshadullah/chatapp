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
    private readonly IDailyMessageQuota _quota;

    /// <summary>
    /// Initializes a new instance of <see cref="ChatService"/>.
    /// </summary>
    public ChatService(IChatRepository repository, IAiProvider aiProvider, IDailyMessageQuota quota)
    {
        _repository = repository;
        _aiProvider = aiProvider;
        _quota = quota;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(Guid ConversationId, string Token)> StreamResponseAsync(
        string content,
        Guid userId,
        Guid? conversationId = null,
        string? modelId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var userMessage = new ChatMessage(content, MessageRole.User);
        RequireUser(userId);

        var existing = conversationId.HasValue
            ? await _repository.GetConversationAsync(conversationId.Value, userId, ct)
              ?? throw new ConversationNotFoundException(conversationId.Value)
            : null;

        // Counted only once the message is accepted, and before anything is created or saved,
        // so a refused message leaves nothing behind.
        if (!await _quota.TryConsumeAsync(userId, ct))
            throw new DailyMessageLimitReachedException(_quota.Limit);

        var conversation = existing ?? await _repository.CreateConversationAsync(userId, ConversationTitle.From(content), ct);

        await _repository.AddMessageAsync(conversation.Id, userId, userMessage, ct);

        // Reload so AI receives full history including the just-saved user message
        var updated = await _repository.GetConversationAsync(conversation.Id, userId, ct)
            ?? throw new InvalidOperationException("Conversation disappeared after insert.");

        var reply = new StringBuilder();
        try
        {
            await foreach (var token in _aiProvider.StreamCompletionAsync(modelId, updated.Messages, ct))
            {
                reply.Append(token);
                yield return (conversation.Id, token);
            }
        }
        finally
        {
            // Also runs when the model fails or the caller leaves, so a part-way reply is kept;
            // by then ct may be cancelled, and the save must still happen.
            if (reply.Length > 0)
            {
                var assistantMessage = new ChatMessage(reply.ToString(), MessageRole.Assistant);
                await _repository.AddMessageAsync(conversation.Id, userId, assistantMessage, CancellationToken.None);
            }
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
