using Chat.Billing.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Billing.Infrastructure.Data;

/// <summary>MongoDB document representation of a Subscription entity.</summary>
// Fields a newer version adds must not stop an older one from reading the subscription.
[BsonIgnoreExtraElements]
public class SubscriptionDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public string StripeSubscriptionId { get; set; } = string.Empty;

    /// <summary>Missing on documents written before cancelling waited for the period's end.</summary>
    public bool CancelAtPeriodEnd { get; set; }
}
