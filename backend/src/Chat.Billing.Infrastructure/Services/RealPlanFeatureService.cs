using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Infrastructure.Services;

/// <summary>
/// Real implementation of <see cref="IPlanFeatureService"/> backed by subscription and plan data.
/// Replaces <see cref="StubPlanFeatureService"/> once billing is wired up.
/// </summary>
public class RealPlanFeatureService : IPlanFeatureService
{
    private static readonly HashSet<Feature> FreeTierFeatures = [Feature.Chat];

    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPlanRepository _plans;

    public RealPlanFeatureService(ISubscriptionRepository subscriptions, IPlanRepository plans)
    {
        _subscriptions = subscriptions;
        _plans = plans;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default)
    {
        if (!Enum.TryParse<Feature>(feature, ignoreCase: true, out var featureEnum))
            return false;

        var subscription = await _subscriptions.GetByUserIdAsync(userId, ct);

        var isActive = subscription is not null &&
            subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing;

        if (!isActive)
            return FreeTierFeatures.Contains(featureEnum);

        var plan = await _plans.GetByIdAsync(subscription!.PlanId, ct);
        return plan?.Includes(featureEnum) ?? false;
    }
}
