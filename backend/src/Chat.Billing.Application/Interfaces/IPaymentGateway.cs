using Chat.Billing.Application.DTOs;

namespace Chat.Billing.Application.Interfaces;

/// <summary>Abstraction over the payment provider (Stripe). Application layer has no Stripe references.</summary>
public interface IPaymentGateway
{
    /// <summary>Creates a hosted checkout session and returns the redirect URL.</summary>
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl,
        CancellationToken ct = default);

    /// <summary>Cancels the Stripe subscription immediately.</summary>
    Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default);
}
