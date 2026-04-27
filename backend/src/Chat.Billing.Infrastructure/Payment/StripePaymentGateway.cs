using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Chat.Billing.Infrastructure.Payment;

/// <summary>
/// Stripe implementation of <see cref="IPaymentGateway"/>.
/// All Stripe SDK calls are isolated here; Application and Domain have no Stripe references.
/// </summary>
public class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeSettings _settings;

    public StripePaymentGateway(IOptions<StripeSettings> settings)
    {
        _settings = settings.Value;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid userId, string stripePriceId, string successUrl, string cancelUrl,
        CancellationToken ct = default)
    {
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = stripePriceId,
                    Quantity = 1,
                },
            ],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = userId.ToString(),
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return new CheckoutSessionDto(session.Url, session.Id);
    }

    /// <inheritdoc />
    public async Task CancelSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        var service = new SubscriptionService();
        await service.CancelAsync(stripeSubscriptionId, cancellationToken: ct);
    }
}
