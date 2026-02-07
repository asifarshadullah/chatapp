using Chat.Application.DTOs;

namespace Chat.Application.Interfaces;

/// <summary>
/// Defines the contract for the chat service that processes user messages.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Processes a user message and returns a response.
    /// </summary>
    /// <param name="content">The user's message content.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ChatResponseDto"/> containing the response message.</returns>
    /// <exception cref="ArgumentException">Thrown when content is null, empty, or whitespace.</exception>
    Task<ChatResponseDto> SendMessageAsync(string content, CancellationToken cancellationToken = default);
}
