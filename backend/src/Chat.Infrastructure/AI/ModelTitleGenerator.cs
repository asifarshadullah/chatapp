using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.AI;

/// <summary>Asks the model that answered a conversation's first message for its title.</summary>
public class ModelTitleGenerator : ITitleGenerator
{
    private const string Instructions =
        "Write a title of at most six words for a conversation that begins with the user's message. " +
        "Reply with the title only.";

    // Enough to recognise what a conversation is about, while bounding the tokens a title costs.
    private const int MaxMessageLength = 1000;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IAiProvider _provider;
    private readonly AiSettings _settings;
    private readonly ILogger<ModelTitleGenerator> _logger;
    private readonly TimeSpan _timeout;

    public ModelTitleGenerator(IAiProvider provider, IOptions<AiSettings> settings, ILogger<ModelTitleGenerator> logger)
        : this(provider, settings.Value, logger, DefaultTimeout)
    {
    }

    /// <param name="timeout">How long to wait for the model; tests pass a short one.</param>
    public ModelTitleGenerator(IAiProvider provider, AiSettings settings, ILogger<ModelTitleGenerator> logger, TimeSpan timeout)
    {
        _provider = provider;
        _settings = settings;
        _logger = logger;
        _timeout = timeout;
    }

    /// <inheritdoc/>
    public async Task<string?> GenerateAsync(string? modelId, string firstMessage, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            var answer = await _provider.CompleteAsync(
                modelId, Instructions, Truncate(firstMessage), _settings.MaxTitleTokens, timeout.Token);
            var title = ConversationTitle.Clean(answer);
            return title.Length > 0 ? title : null;
        }
        catch (ModelRateLimitedException ex)
        {
            _logger.LogWarning(ex, "The model service refused a title at its rate limit");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("The model gave no title within {Timeout}", _timeout);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "The title could not be generated");
        }
        return null;
    }

    private static string Truncate(string message) =>
        message.Length <= MaxMessageLength
            ? message
            : message[..(char.IsHighSurrogate(message[MaxMessageLength - 1]) ? MaxMessageLength - 1 : MaxMessageLength)];
}
