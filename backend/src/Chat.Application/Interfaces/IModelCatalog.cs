using Chat.Application.DTOs;

namespace Chat.Application.Interfaces;

/// <summary>The model options this deployment offers, and which one answers by default.</summary>
public interface IModelCatalog
{
    string DefaultId { get; }

    IReadOnlyList<ModelSummary> Offered { get; }

    bool IsOffered(string id);
}
