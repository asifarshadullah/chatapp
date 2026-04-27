using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Infrastructure.Data;
using MongoDB.Driver;

namespace Chat.Billing.Infrastructure.Repositories;

/// <summary>MongoDB-backed implementation of <see cref="IPlanRepository"/>.</summary>
public class MongoPlanRepository : IPlanRepository
{
    private readonly IMongoCollection<PlanDocument> _collection;

    public MongoPlanRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<PlanDocument>("plans");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken ct = default)
    {
        var docs = await _collection.Find(_ => true).ToListAsync(ct);
        return docs.Select(ToEntity).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await _collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToEntity(doc);
    }

    private static Plan ToEntity(PlanDocument doc) =>
        new(doc.Id, doc.Name, doc.Tier, doc.PricePerMonth, doc.Features);
}
