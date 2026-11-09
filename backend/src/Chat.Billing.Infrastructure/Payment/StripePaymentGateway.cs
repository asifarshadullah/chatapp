using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Enums;
using PlanTier = Chat.Billing.Domain.Enums.PlanTier;
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
    /// <summary>The subscription metadata key that names the user the subscription belongs to.</summary>
    public const string UserIdMetadataKey = "userId";

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
            // Every later event is about the subscription, not the checkout, so it carries the user.
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = userId.ToString() },
            },
        };

        try
        {
            var session = await new SessionService().CreateAsync(options, cancellationToken: ct);
            return new CheckoutSessionDto(session.Url, session.Id);
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException("Stripe could not create the checkout.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        Subscription subscription;
        try
        {
            subscription = await new SubscriptionService().GetAsync(stripeSubscriptionId, cancellationToken: ct);
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            throw new SubscriptionNotFoundAtGatewayException(stripeSubscriptionId);
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException("Stripe could not be asked for the subscription.", ex);
        }

        var userId = subscription.Metadata.TryGetValue(UserIdMetadataKey, out var raw) && Guid.TryParse(raw, out var parsed)
            ? parsed
            : (Guid?)null;
        var priceId = subscription.Items?.Data.FirstOrDefault()?.Price?.Id;
        return new StripeSubscriptionSnapshot(subscription.Id, userId, TierOf(priceId), ToStatus(subscription.Status),
            DateTime.SpecifyKind(subscription.CurrentPeriodEnd, DateTimeKind.Utc), subscription.CancelAtPeriodEnd);
    }

    /// <summary>
    /// Maps Stripe's subscription status to the four the app keeps. A status Stripe adds later
    /// grants nothing until it is mapped here.
    /// </summary>
    public static SubscriptionStatus ToStatus(string stripeStatus) => stripeStatus switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Cancelled,
        _ => SubscriptionStatus.PastDue,
    };

    private PlanTier? TierOf(string? priceId)
    {
        var tier = _settings.PriceIds.FirstOrDefault(p => p.Value == priceId).Key;
        return priceId is not null && Enum.TryParse<PlanTier>(tier, out var parsed) ? parsed : null;
    }

    /// <inheritdoc />
    public async Task CancelAtPeriodEndAsync(string stripeSubscriptionId, CancellationToken ct = default)
    {
        try
        {
            await new SubscriptionService().UpdateAsync(stripeSubscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException("Stripe could not cancel the subscription.", ex);
        }
    }
}
