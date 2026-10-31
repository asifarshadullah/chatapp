using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Domain.Enums;
using Chat.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using DomainMessage = Chat.Domain.Entities.ChatMessage;

namespace Chat.Infrastructure.AI;

/// <summary>
/// Streams replies from any service offering the OpenAI chat completions API: Groq, OpenRouter,
/// Gemini, Ollama's /v1 endpoint and others. Each offered option gets its own client.
/// </summary>
public class OpenAiCompatibleProvider : IAiProvider
{
    private readonly AiSettings _settings;
    private readonly Dictionary<string, (ChatClient Client, ModelOption Option)> _clients;

    public OpenAiCompatibleProvider(IOptions<AiSettings> settings)
        : this(settings.Value, new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    /// <param name="httpClient">Shared by every option's client; tests pass one with a fake handler.</param>
    public OpenAiCompatibleProvider(AiSettings settings, HttpClient httpClient)
    {
        _settings = settings;
        _clients = settings.Models
            .Where(m => m.Value.IsOffered)
            .ToDictionary(
                m => m.Key,
                m => (CreateClient(m.Value, httpClient), m.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static ChatClient CreateClient(ModelOption option, HttpClient httpClient) =>
        new(option.Model, new ApiKeyCredential(option.ApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(option.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
            // A retry turns a clear "try again later" into a long silence and spends more of the
            // service's per-minute allowance.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        });

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        string? modelId,
        IReadOnlyList<DomainMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (client, option) = _clients[modelId ?? _settings.DefaultModel];

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(_settings.SystemPrompt))
            messages.Add(new SystemChatMessage(_settings.SystemPrompt));
        // Only the latest messages, so a request stays within a free tier's per-minute token limit.
        messages.AddRange(history.TakeLast(_settings.MaxHistoryMessages).Select(ToRequestMessage));

        var options = Options(option, _settings.MaxReplyTokens);

        await using var updates = client.CompleteChatStreamingAsync(messages, options, ct).GetAsyncEnumerator(ct);
        while (await MoveNextAsync(updates, option.ApiKey))
        {
            foreach (var part in updates.Current.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(
        string? modelId, string instructions, string content, int maxTokens, CancellationToken ct = default)
    {
        var (client, option) = _clients[modelId ?? _settings.DefaultModel];
        ChatMessage[] messages = [new SystemChatMessage(instructions), new UserChatMessage(content)];

        try
        {
            ChatCompletion completion = await client.CompleteChatAsync(messages, Options(option, maxTokens), ct);
            return string.Concat(completion.Content.Select(part => part.Text));
        }
        catch (ClientResultException ex)
        {
            throw WithoutKey(ex, option.ApiKey);
        }
    }

    private static ChatCompletionOptions Options(ModelOption option, int maxTokens)
    {
        var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        if (!string.IsNullOrEmpty(option.ReasoningEffort))
        {
            // Marked experimental by the SDK; the provider tests pin down the request it produces.
#pragma warning disable OPENAI001
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(option.ReasoningEffort);
#pragma warning restore OPENAI001
        }
        return options;
    }

    private static async Task<bool> MoveNextAsync(IAsyncEnumerator<StreamingChatCompletionUpdate> updates, string apiKey)
    {
        try
        {
            return await updates.MoveNextAsync();
        }
        catch (ClientResultException ex)
        {
            throw WithoutKey(ex, apiKey);
        }
    }

    // The service's error is logged by the caller, and some services echo the key they were sent,
    // so it is rethrown with the key removed and without the original exception.
    private static Exception WithoutKey(ClientResultException ex, string apiKey)
    {
        var error = ex.Message.Replace(apiKey, "[key]", StringComparison.Ordinal);
        return ex.Status == 429 ? new ModelRateLimitedException(error) : new ModelServiceException(ex.Status, error);
    }

    private static ChatMessage ToRequestMessage(DomainMessage message) =>
        message.Role == MessageRole.User
            ? new UserChatMessage(message.Content)
            : new AssistantChatMessage(message.Content);
}
