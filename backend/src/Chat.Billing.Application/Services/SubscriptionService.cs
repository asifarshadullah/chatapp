using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;

namespace Chat.Billing.Application.Services;

/// <summary>Manages subscription lifecycle: checkout, cancellation, and webhook event handling.</summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IPaymentGateway _gateway;
    private readonly ISubscriptionRepository _subscriptions;

    public SubscriptionService(IPaymentGateway gateway, ISubscriptionRepository subscriptions)
    {
        _gateway = gateway;
        _subscriptions = subscriptions;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionDto> SubscribeAsync(
        Guid userId, Guid planId, string stripePriceId,
        string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        return await _gateway.CreateCheckoutSessionAsync(userId, stripePriceId, successUrl, cancelUrl, ct);
    }

    /// <inheritdoc />
    public async Task CancelSubscriptionAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException("No active subscription found for user.");

        await _gateway.CancelSubscriptionAsync(subscription.StripeSubscriptionId, ct);
        subscription.Cancel();
        await _subscriptions.SaveAsync(subscription, ct);
    }

    /// <inheritdoc />
    public async Task HandlePaymentSucceededAsync(
        string stripeSubscriptionId, DateTime newPeriodEnd, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByStripeIdAsync(stripeSubscriptionId, ct);
        if (subscription is null) return; // unknown subscription — ignore

        if (subscription.Status == SubscriptionStatus.Active &&
            subscription.CurrentPeriodEnd == newPeriodEnd) return; // idempotent

        subscription.Activate(newPeriodEnd);
        await _subscriptions.SaveAsync(subscription, ct);
    }

    /// <inheritdoc />
    public async Task HandleSubscriptionCancelledAsync(
        string stripeSubscriptionId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByStripeIdAsync(stripeSubscriptionId, ct);
        if (subscription is null) return;

        if (subscription.Status == SubscriptionStatus.Cancelled) return; // idempotent

        subscription.Cancel();
        await _subscriptions.SaveAsync(subscription, ct);
    }

    /// <inheritdoc />
    public async Task HandlePaymentFailedAsync(
        string stripeSubscriptionId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByStripeIdAsync(stripeSubscriptionId, ct);
        if (subscription is null) return;

        subscription.MarkPastDue();
        await _subscriptions.SaveAsync(subscription, ct);
    }

    /// <inheritdoc />
    public async Task<SubscriptionStatusDto?> GetSubscriptionStatusAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetByUserIdAsync(userId, ct);
        return subscription is null ? null : new SubscriptionStatusDto(
            PlanName: subscription.PlanId.ToString(), // Plan name resolved in controller via IPlanRepository
            Tier: string.Empty,
            Status: subscription.Status.ToString(),
            CurrentPeriodEnd: subscription.CurrentPeriodEnd);
    }
}
