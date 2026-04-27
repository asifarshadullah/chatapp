using Chat.Billing.Application.Interfaces;
using Chat.Billing.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Stripe;

namespace Chat.Billing.Infrastructure.Payment;

/// <summary>
/// Validates Stripe webhook signatures and routes events to <see cref="ISubscriptionService"/>.
/// Separated from <see cref="StripePaymentGateway"/> to avoid a circular DI dependency.
/// </summary>
public class StripeWebhookHandler : IWebhookHandler
{
    private readonly StripeSettings _settings;
    private readonly ISubscriptionService _subscriptionService;

    public StripeWebhookHandler(IOptions<StripeSettings> settings, ISubscriptionService subscriptionService)
    {
        _settings = settings.Value;
        _subscriptionService = subscriptionService;
    }

    /// <inheritdoc />
    public async Task HandleAsync(string payload, string signatureHeader, CancellationToken ct = default)
    {
        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _settings.WebhookSecret);

        switch (stripeEvent.Type)
        {
            case "invoice.payment_succeeded":
            {
                var invoice = stripeEvent.Data.Object as Invoice;
                if (invoice?.SubscriptionId is not null)
                {
                    var periodEnd = invoice.Lines.Data.FirstOrDefault()?.Period?.End ?? DateTime.UtcNow.AddMonths(1);
                    await _subscriptionService.HandlePaymentSucceededAsync(invoice.SubscriptionId, periodEnd, ct);
                }
                break;
            }

            case "customer.subscription.deleted":
            {
                var sub = stripeEvent.Data.Object as Subscription;
                if (sub?.Id is not null)
                    await _subscriptionService.HandleSubscriptionCancelledAsync(sub.Id, ct);
                break;
            }

            case "invoice.payment_failed":
            {
                var invoice = stripeEvent.Data.Object as Invoice;
                if (invoice?.SubscriptionId is not null)
                    await _subscriptionService.HandlePaymentFailedAsync(invoice.SubscriptionId, ct);
                break;
            }
        }
    }
}
