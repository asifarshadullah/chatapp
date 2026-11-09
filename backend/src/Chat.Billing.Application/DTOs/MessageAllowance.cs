namespace Chat.Billing.Application.DTOs;

/// <summary>
/// How many messages a user's plan allows per UTC day, and how many Pro would allow when the user
/// is on Free.
/// </summary>
public record MessageAllowance(int Limit, int? UpgradeLimit);
