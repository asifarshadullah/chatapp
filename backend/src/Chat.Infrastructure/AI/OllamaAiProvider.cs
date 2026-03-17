using System.Runtime.CompilerServices;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace Chat.Infrastructure.AI;

/// <summary>
/// Streams AI completions from a locally-running Ollama instance.
/// Implements IAiProvider — the Application layer never sees Ollama-specific types.
/// </summary>
public class OllamaAiProvider : IAiProvider
{
    private readonly OllamaApiClient _client;
    private readonly string _model;
    private readonly string _systemPrompt;

    /// <summary>
    /// Initializes OllamaAiProvider with connection settings from appsettings.
    /// </summary>
    public OllamaAiProvider(IOptions<OllamaSettings> settings)
    {
        _client = new OllamaApiClient(new Uri(settings.Value.BaseUrl));
        _model = settings.Value.Model;
        _systemPrompt = settings.Value.SystemPrompt;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<Message>();

        if (!string.IsNullOrWhiteSpace(_systemPrompt))
            messages.Add(new Message { Role = ChatRole.System, Content = _systemPrompt });

        messages.AddRange(history.Select(m => new Message
        {
            Role = m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant,
            Content = m.Content
        }));

        var request = new ChatRequest
        {
            Model = _model,
            Messages = messages,
            Stream = true
        };

        await foreach (var response in _client.ChatAsync(request, ct))
        {
            var token = response?.Message?.Content;
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }
}
