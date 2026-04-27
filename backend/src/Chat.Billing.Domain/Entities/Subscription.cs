using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Domain.Entities;

/// <summary>Represents a user's subscription to a billing plan.</summary>
public class Subscription
{
    public Guid Id { get; }
    public Guid UserId { get; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime CurrentPeriodEnd { get; private set; }
    public string StripeSubscriptionId { get; }

    /// <summary>Creates a new subscription (starts in Trialing status).</summary>
    public Subscription(Guid userId, Guid planId, string stripeSubscriptionId, DateTime periodEnd)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        PlanId = planId;
        StripeSubscriptionId = stripeSubscriptionId;
        Status = SubscriptionStatus.Trialing;
        CurrentPeriodEnd = periodEnd;
    }

    /// <summary>Reconstructs a subscription from persistent storage.</summary>
    public Subscription(Guid id, Guid userId, Guid planId, SubscriptionStatus status,
        DateTime periodEnd, string stripeSubscriptionId)
    {
        Id = id;
        UserId = userId;
        PlanId = planId;
        Status = status;
        CurrentPeriodEnd = periodEnd;
        StripeSubscriptionId = stripeSubscriptionId;
    }

    /// <summary>Activates the subscription. Throws if already cancelled.</summary>
    public void Activate(DateTime newPeriodEnd)
    {
        if (Status == SubscriptionStatus.Cancelled)
            throw new InvalidOperationException("Cannot activate a cancelled subscription.");
        Status = SubscriptionStatus.Active;
        CurrentPeriodEnd = newPeriodEnd;
    }

    /// <summary>Cancels the subscription.</summary>
    public void Cancel() => Status = SubscriptionStatus.Cancelled;

    /// <summary>Marks the subscription as past due.</summary>
    public void MarkPastDue() => Status = SubscriptionStatus.PastDue;

    /// <summary>Switches the subscription to a different plan.</summary>
    public void ChangePlan(Guid newPlanId) => PlanId = newPlanId;
}
