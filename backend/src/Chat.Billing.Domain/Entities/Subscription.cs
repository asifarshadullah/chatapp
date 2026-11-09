using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Domain.Entities;

/// <summary>
/// A user's subscription to a paid plan, as Stripe last reported it. Each user has at most one.
/// </summary>
public class Subscription
{
    public Guid Id { get; }
    public Guid UserId { get; }
    public Guid PlanId { get; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime CurrentPeriodEnd { get; }
    public string StripeSubscriptionId { get; }

    /// <summary>Whether the subscription ends when the current period does, rather than renewing.</summary>
    public bool CancelAtPeriodEnd { get; }

    /// <summary>Whether the user has the subscription's plan now; otherwise they are on Free.</summary>
    public bool GrantsPlan => Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing;

    public Subscription(Guid id, Guid userId, Guid planId, SubscriptionStatus status,
        DateTime periodEnd, string stripeSubscriptionId, bool cancelAtPeriodEnd = false)
    {
        Id = id;
        UserId = userId;
        PlanId = planId;
        Status = status;
        CurrentPeriodEnd = periodEnd;
        StripeSubscriptionId = stripeSubscriptionId;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
    }

    /// <summary>Cancels the subscription.</summary>
    public void Cancel() => Status = SubscriptionStatus.Cancelled;
}
