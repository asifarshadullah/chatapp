namespace Chat.Billing.Application.Interfaces;

/// <summary>
/// Checks whether a billing feature is enabled for the given user's current plan.
/// </summary>
public interface IPlanFeatureService
{
    /// <summary>Returns true if the named feature is enabled for the user.</summary>
    Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default);
}
