using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.AI;

/// <inheritdoc/>
public class ModelCatalog : IModelCatalog
{
    private readonly AiSettings _settings;

    public ModelCatalog(IOptions<AiSettings> settings)
    {
        _settings = settings.Value;
        Offered = _settings.Models
            .Where(m => m.Value.IsOffered)
            .Select(m => new ModelSummary(m.Key, m.Value.Name))
            .ToList();
        // The option's own id, so it matches a listed one exactly whatever case the setting used.
        DefaultId = _settings.Models.Keys.FirstOrDefault(
            id => string.Equals(id, _settings.DefaultModel, StringComparison.OrdinalIgnoreCase)) ?? _settings.DefaultModel;
    }

    public string DefaultId { get; }

    public IReadOnlyList<ModelSummary> Offered { get; }

    public bool IsOffered(string id) =>
        _settings.Models.TryGetValue(id, out var option) && option.IsOffered;
}
