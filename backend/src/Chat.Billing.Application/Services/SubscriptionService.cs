using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Application.Services;

/// <summary>Manages subscription lifecycle: checkout, cancellation, and webhook event handling.</summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IPaymentGateway _gateway;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPlanRepository _plans;

    public SubscriptionService(IPaymentGateway gateway, ISubscriptionRepository subscriptions, IPlanRepository plans)
    {
        _gateway = gateway;
        _subscriptions = subscriptions;
        _plans = plans;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionDto> SubscribeAsync(
        Guid userId, Guid planId, string stripePriceId,
        string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        var existing = await _subscriptions.GetByUserIdAsync(userId, ct);
        if (existing is { GrantsPlan: true })
            throw new AlreadySubscribedException();

        return await _gateway.CreateCheckoutSessionAsync(userId, stripePriceId, successUrl, cancelUrl, ct);
    }

    /// <inheritdoc />
    public async Task CancelSubscriptionAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByUserIdAsync(userId, ct);
        if (subscription is not { GrantsPlan: true })
            throw new InvalidOperationException("No active subscription found for user.");

        await _gateway.CancelAtPeriodEndAsync(subscription.StripeSubscriptionId, ct);
        // Read back now, so the page shows when the plan ends without waiting for the webhook.
        await SyncSubscriptionAsync(subscription.StripeSubscriptionId, ct);
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        StripeSubscriptionSnapshot atStripe;
        try
        {
            atStripe = await _gateway.GetSubscriptionAsync(stripeSubscriptionId, ct);
        }
        catch (SubscriptionNotFoundAtGatewayException)
        {
            return SyncResult.MissingAtGateway;
        }

        if (atStripe.UserId is not { } userId)
            return SyncResult.NotTiedToUser;
        if (atStripe.Tier is not { } tier || await _plans.GetByTierAsync(tier, ct) is not { } plan)
            return SyncResult.UnknownPlan;

        var incoming = new Subscription(Guid.NewGuid(), userId, plan.Id, atStripe.Status, atStripe.CurrentPeriodEnd,
            atStripe.Id, atStripe.CancelAtPeriodEnd);
        var stored = await _subscriptions.GetByUserIdAsync(userId, ct);
        // A late event about a user's old subscription must not take away the plan their new one gives.
        if (stored is not null && stored.StripeSubscriptionId != atStripe.Id && stored.GrantsPlan && !incoming.GrantsPlan)
            return SyncResult.Superseded;

        await _subscriptions.SaveAsync(incoming, ct);
        return SyncResult.Saved;
    }

    /// <inheritdoc />
    public async Task<SubscriptionStatusDto> GetSubscriptionStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByUserIdAsync(userId, ct);
        if (subscription is { GrantsPlan: true } && await _plans.GetByIdAsync(subscription.PlanId, ct) is { } plan)
            return new SubscriptionStatusDto(plan.Name, plan.Tier.ToString(), subscription.Status.ToString(),
                subscription.CurrentPeriodEnd, subscription.CancelAtPeriodEnd);

        var free = await _plans.GetByTierAsync(PlanTier.Free, ct);
        return new SubscriptionStatusDto(free?.Name ?? "Free", nameof(PlanTier.Free), null, null, false);
    }
}
