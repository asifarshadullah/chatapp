using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;

namespace Chat.Application.Services;

/// <summary>
/// Processes chat messages and returns echo responses.
/// </summary>
public class ChatService : IChatService
{
    /// <summary>
    /// Processes a user message and returns an echo response.
    /// </summary>
    /// <param name="content">The user's message content.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ChatResponseDto"/> containing the echo response.</returns>
    /// <exception cref="ArgumentException">Thrown when content is null, empty, or whitespace.</exception>
    public Task<ChatResponseDto> SendMessageAsync(string content, CancellationToken cancellationToken = default)
    {
        // Validate the user's input first (domain entity validates constructed content, not raw input)
        var userMessage = new ChatMessage(content, MessageRole.User);

        // Create the echo response
        var echoMessage = new ChatMessage($"Echo: {userMessage.Content}", MessageRole.Assistant);

        var response = new ChatResponseDto(
            echoMessage.Id,
            echoMessage.Content,
            echoMessage.Role.ToString().ToLowerInvariant(),
            echoMessage.Timestamp);

        return Task.FromResult(response);
    }
}
