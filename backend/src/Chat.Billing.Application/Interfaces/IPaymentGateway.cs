using Chat.Billing.Application.DTOs;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Abstraction over the payment provider (Stripe). Application layer has no Stripe references.</summary>
public interface IPaymentGateway
{
    /// <summary>Creates a hosted checkout session and returns the redirect URL.</summary>
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Reads a subscription as the provider holds it now. Throws
    /// <see cref="Exceptions.SubscriptionNotFoundAtGatewayException"/> when it has none with this id,
    /// and <see cref="Exceptions.PaymentGatewayException"/> when it cannot be reached.
    /// </summary>
    Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Asks for the subscription to end when its current period does, instead of renewing.</summary>
    Task CancelAtPeriodEndAsync(string stripeSubscriptionId, CancellationToken ct = default);
}
