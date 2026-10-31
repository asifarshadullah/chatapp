namespace Chat.Application.Interfaces;

/// <summary>Writes a short title for a conversation from the message that started it.</summary>
public interface ITitleGenerator
{
    /// <summary>
    /// Asks the given model option for a title. Never throws: a failure, a timeout or an empty
    /// answer gives null, so a title can never affect the reply it follows.
    /// </summary>
    Task<string?> GenerateAsync(string? modelId, string firstMessage, CancellationToken ct = default);
}
