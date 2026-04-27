namespace Chat.Billing.Application.DTOs;

/// <summary>Plan information returned to API consumers.</summary>
public record PlanDto(Guid Id, string Name, string Tier, decimal PricePerMonth, IReadOnlyList<string> Features);
