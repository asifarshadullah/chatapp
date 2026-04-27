using Chat.Billing.Domain.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Billing.Infrastructure.Data;

/// <summary>MongoDB document representation of a Plan entity.</summary>
public class PlanDocument
{
    [BsonId]
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PlanTier Tier { get; set; }
    public decimal PricePerMonth { get; set; }
    public List<Feature> Features { get; set; } = [];
    public string StripePriceId { get; set; } = string.Empty;
}
