namespace Chat.Billing.Infrastructure.Configuration;

/// <summary>Stripe API configuration. Bind from appsettings "Stripe" section.</summary>
public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Maps plan tier name (e.g. "Pro") to Stripe price ID (e.g. "price_xxx").</summary>
    public Dictionary<string, string> PriceIds { get; set; } = new();
}
