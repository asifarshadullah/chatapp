namespace Chat.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed settings for the Ollama connection, bound from appsettings.
/// </summary>
public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma2:2b";
    public string SystemPrompt { get; set; } =
        "You are a concise, helpful assistant. Respond in plain text without markdown formatting. " +
        "Keep answers short and direct unless the user explicitly asks for detail.";
}
