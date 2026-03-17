using Chat.Domain.Entities;

namespace Chat.Application.Interfaces;

/// <summary>
/// Provides streaming AI completions given a conversation history.
/// The implementation lives in Infrastructure — Application only sees this contract.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Streams completion tokens for the given conversation history.
    /// Tokens are yielded as they arrive from the model.
    /// </summary>
    /// <param name="history">Full conversation history including the latest user message.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}
