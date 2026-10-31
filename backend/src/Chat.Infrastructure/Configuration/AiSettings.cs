namespace Chat.Infrastructure.Configuration;

/// <summary>
/// The models the app can answer with, bound from the <c>Ai</c> section. An option is offered only
/// when its key is set, which is how each environment decides what it offers.
/// </summary>
public class AiSettings
{
    public string DefaultModel { get; set; } = string.Empty;

    public Dictionary<string, ModelOption> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string SystemPrompt { get; set; } =
        "You are a concise, helpful assistant. Respond in plain text without markdown formatting. " +
        "Keep answers short and direct unless the user explicitly asks for detail.";

    /// <summary>How many of a conversation's latest messages each request carries.</summary>
    public int MaxHistoryMessages { get; set; } = 10;

    public int MaxReplyTokens { get; set; } = 800;

    /// <summary>
    /// The most tokens a title request may use. Reasoning counts, so this leaves a reasoning model
    /// room to think before it writes the few words of a title.
    /// </summary>
    public int MaxTitleTokens { get; set; } = 300;
}

/// <summary>One service offering the OpenAI chat completions API, and the model to use there.</summary>
public class ModelOption
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    private string _apiKey = string.Empty;

    /// <summary>Trimmed, since a secret file saved with Windows line endings keeps a trailing \r.</summary>
    public string ApiKey
    {
        get => _apiKey;
        set => _apiKey = value?.Trim() ?? string.Empty;
    }

    public string Model { get; set; } = string.Empty;

    /// <summary><c>low</c>, <c>medium</c> or <c>high</c> for models that reason; empty sends none.</summary>
    public string ReasoningEffort { get; set; } = string.Empty;

    public bool IsOffered => !string.IsNullOrEmpty(ApiKey);
}
