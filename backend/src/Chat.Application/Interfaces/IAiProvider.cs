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
    /// <param name="modelId">The configured model option to answer with, or null for the default.</param>
    /// <param name="history">Full conversation history including the latest user message.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<string> StreamCompletionAsync(
        string? modelId,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    /// <summary>
    /// Asks for one answer, unstreamed, to a single message under the given instruction. Neither the
    /// app's system prompt nor any conversation history is sent.
    /// </summary>
    /// <param name="modelId">The configured model option to answer with, or null for the default.</param>
    /// <param name="instructions">Sent as the only system message.</param>
    /// <param name="content">Sent as the only user message.</param>
    /// <param name="maxTokens">The most tokens the answer may use, reasoning included.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The answer's text, which may be empty.</returns>
    Task<string> CompleteAsync(
        string? modelId,
        string instructions,
        string content,
        int maxTokens,
        CancellationToken ct = default);
}
