namespace Chat.Billing.Application.DTOs;

/// <summary>Current subscription status returned to the API consumer.</summary>
public record SubscriptionStatusDto(string PlanName, string Tier, string Status, DateTime CurrentPeriodEnd);
