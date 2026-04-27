using Chat.Billing.Domain.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Billing.Infrastructure.Data;

/// <summary>MongoDB document representation of a Subscription entity.</summary>
public class SubscriptionDocument
{
    [BsonId]
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public string StripeSubscriptionId { get; set; } = string.Empty;
}
