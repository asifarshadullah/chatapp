using Chat.Billing.Domain.Entities;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Persistence interface for Plan entities.</summary>
public interface IPlanRepository
{
    /// <summary>Returns all available plans.</summary>
    Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a plan by its ID, or null if not found.</summary>
    Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
