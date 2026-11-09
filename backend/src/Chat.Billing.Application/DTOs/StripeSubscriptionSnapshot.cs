using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Application.DTOs;

/// <summary>
/// A subscription as the payment provider holds it now. <see cref="UserId"/> is null when it was
/// not started from the app, and <see cref="Tier"/> when its price is not a configured plan's.
/// </summary>
public record StripeSubscriptionSnapshot(
    string Id,
    Guid? UserId,
    PlanTier? Tier,
    SubscriptionStatus Status,
    DateTime CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
