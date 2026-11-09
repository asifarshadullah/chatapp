using Chat.Billing.Application.DTOs;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Application service for managing subscriptions.</summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Creates a Stripe Checkout session for the given plan. Throws
    /// <see cref="Exceptions.AlreadySubscribedException"/> when the user already has a paid plan.
    /// </summary>
    Task<CheckoutSessionDto> SubscribeAsync(Guid userId, Guid planId, string stripePriceId, string successUrl, string cancelUrl, CancellationToken ct = default);

    /// <summary>
    /// Ends the user's paid plan when its current period does; until then they keep it. Throws
    /// <see cref="InvalidOperationException"/> when they have no paid plan.
    /// </summary>
    Task CancelSubscriptionAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Reads the subscription from the payment provider and stores it for the user it belongs to.
    /// Throws <see cref="Exceptions.PaymentGatewayException"/> when the provider cannot be reached.
    /// </summary>
    Task<SyncResult> SyncSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Returns the user's current plan, which is Free when no subscription grants another.</summary>
    Task<SubscriptionStatusDto> GetSubscriptionStatusAsync(Guid userId, CancellationToken ct = default);
}
