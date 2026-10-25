using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Configuration;

/// <summary>
/// Refuses at startup a model configuration that would fail on every message, such as a deploy
/// that forgot the Groq key or a typo in a URL.
/// </summary>
public class AiSettingsValidator : IValidateOptions<AiSettings>
{
    private static readonly string[] ReasoningEfforts = ["low", "medium", "high"];

    public ValidateOptionsResult Validate(string? name, AiSettings settings)
    {
        var failures = new List<string>();
        var offered = settings.Models.Where(m => m.Value.IsOffered).ToList();

        if (offered.Count == 0)
            failures.Add("Ai:Models: no model option is offered; set the ApiKey of at least one.");
        else if (!settings.Models.TryGetValue(settings.DefaultModel, out var defaultOption) || !defaultOption.IsOffered)
            failures.Add($"Ai:DefaultModel '{settings.DefaultModel}' must name an offered option.");

        foreach (var (id, option) in offered)
        {
            if (!Uri.TryCreate(option.BaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                failures.Add($"Ai:Models:{id}:BaseUrl must be an absolute http or https URL.");
            if (string.IsNullOrWhiteSpace(option.Model))
                failures.Add($"Ai:Models:{id}:Model must be set.");
            if (!string.IsNullOrEmpty(option.ReasoningEffort) && !ReasoningEfforts.Contains(option.ReasoningEffort))
                failures.Add($"Ai:Models:{id}:ReasoningEffort must be low, medium or high.");
        }

        if (settings.MaxHistoryMessages <= 0)
            failures.Add("Ai:MaxHistoryMessages must be greater than zero.");
        if (settings.MaxReplyTokens <= 0)
            failures.Add("Ai:MaxReplyTokens must be greater than zero.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
