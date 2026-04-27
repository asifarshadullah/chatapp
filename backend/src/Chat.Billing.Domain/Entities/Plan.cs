using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Domain.Entities;

/// <summary>Represents a subscription plan with its tier, pricing, and included features.</summary>
public class Plan
{
    public Guid Id { get; }
    public string Name { get; }
    public PlanTier Tier { get; }
    public decimal PricePerMonth { get; }
    public IReadOnlyList<Feature> Features { get; }

    public Plan(Guid id, string name, PlanTier tier, decimal pricePerMonth, IEnumerable<Feature> features)
    {
        Id = id;
        Name = name;
        Tier = tier;
        PricePerMonth = pricePerMonth;
        Features = features.ToList().AsReadOnly();
    }

    /// <summary>Returns true if the plan includes the given feature.</summary>
    public bool Includes(Feature feature) => Features.Contains(feature);
}
