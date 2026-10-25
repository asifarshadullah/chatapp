using Chat.Application.DTOs;

namespace Chat.Api.Contracts;

/// <param name="DefaultModel">The id of the option a message naming none is answered by.</param>
/// <param name="Models">The options this deployment offers, by id and display name only.</param>
public record ModelsResponse(string DefaultModel, IReadOnlyList<ModelSummary> Models);
