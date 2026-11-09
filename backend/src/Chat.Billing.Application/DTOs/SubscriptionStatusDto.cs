namespace Chat.Billing.Application.DTOs;

/// <summary>
/// The user's current plan. On Free, <see cref="Status"/> and <see cref="CurrentPeriodEnd"/> are
/// null; on a paid plan, the period ends on <see cref="CurrentPeriodEnd"/>, renewing then unless
/// <see cref="CancelAtPeriodEnd"/>.
/// </summary>
public record SubscriptionStatusDto(string PlanName, string Tier, string? Status, DateTime? CurrentPeriodEnd, bool CancelAtPeriodEnd);
