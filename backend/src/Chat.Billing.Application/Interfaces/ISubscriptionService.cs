using Chat.Billing.Application.DTOs;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Application service for managing subscriptions.</summary>
public interface ISubscriptionService
{
    /// <summary>Creates a Stripe Checkout session for the given plan.</summary>
    Task<CheckoutSessionDto> SubscribeAsync(Guid userId, Guid planId, string stripePriceId, string successUrl, string cancelUrl, CancellationToken ct = default);

    /// <summary>Cancels the current subscription for the user.</summary>
    Task CancelSubscriptionAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Activates (or re-activates) a subscription after a successful payment.</summary>
    Task HandlePaymentSucceededAsync(string stripeSubscriptionId, DateTime newPeriodEnd, CancellationToken ct = default);

    /// <summary>Marks a subscription as cancelled after Stripe fires the cancellation event.</summary>
    Task HandleSubscriptionCancelledAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Marks a subscription as past due after invoice payment failure.</summary>
    Task HandlePaymentFailedAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Returns the subscription status for a user, or null if no subscription.</summary>
    Task<SubscriptionStatusDto?> GetSubscriptionStatusAsync(Guid userId, CancellationToken ct = default);
}
