using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Chat.Billing.Infrastructure.Payment;

/// <summary>
/// Validates Stripe webhook signatures and, for every event about a subscription, has
/// <see cref="ISubscriptionService"/> read that subscription again. Events can then arrive in any
/// order, or twice, and leave the same result.
/// </summary>
public class StripeWebhookHandler : IWebhookHandler
{
    private readonly StripeSettings _settings;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<StripeWebhookHandler> _logger;

    public StripeWebhookHandler(IOptions<StripeSettings> settings, ISubscriptionService subscriptionService,
        ILogger<StripeWebhookHandler> logger)
    {
        _settings = settings.Value;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(string payload, string signatureHeader, CancellationToken ct = default)
    {
        // Only ids are read from the event, so a dashboard webhook on a newer API version still works.
        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _settings.WebhookSecret,
            throwOnApiVersionMismatch: false);

        var subscriptionId = stripeEvent.Data.Object switch
        {
            Session session when stripeEvent.Type == "checkout.session.completed" => session.SubscriptionId,
            Subscription subscription when stripeEvent.Type.StartsWith("customer.subscription.", StringComparison.Ordinal) => subscription.Id,
            Invoice invoice when stripeEvent.Type is "invoice.paid" or "invoice.payment_succeeded" or "invoice.payment_failed" => invoice.SubscriptionId,
            _ => null,
        };
        if (subscriptionId is null)
            return;

        var result = await _subscriptionService.SyncSubscriptionAsync(subscriptionId, ct);
        if (result != SyncResult.Saved)
            _logger.LogInformation("Stripe event {EventId} ({EventType}) left subscription {SubscriptionId} unchanged: {Result}",
                stripeEvent.Id, stripeEvent.Type, subscriptionId, result);
    }
}
