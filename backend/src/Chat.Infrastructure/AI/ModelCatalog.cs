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
    }

    public string DefaultId => _settings.DefaultModel;

    public IReadOnlyList<ModelSummary> Offered { get; }

    public bool IsOffered(string id) =>
        _settings.Models.TryGetValue(id, out var option) && option.IsOffered;
}
