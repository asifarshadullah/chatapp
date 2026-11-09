namespace Chat.Billing.Infrastructure.Configuration;

/// <summary>Stripe API configuration. Bind from appsettings "Stripe" section.</summary>
public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Maps plan tier name (e.g. "Pro") to Stripe price ID (e.g. "price_xxx").</summary>
    public Dictionary<string, string> PriceIds { get; set; } = new();

    /// <summary>A live key takes real money, so it is refused at startup unless this is set.</summary>
    public bool AllowLiveKey { get; set; }

    /// <summary>
    /// Off until the key, the webhook secret and Pro's price are all set: checkout without any of
    /// them would fail at Stripe or never reach the user's account.
    /// </summary>
    public CheckoutMode Mode
    {
        get
        {
            PriceIds.TryGetValue("Pro", out var proPrice);
            if (!IsSet(SecretKey) || !IsSet(WebhookSecret) || !IsSet(proPrice))
                return CheckoutMode.Off;
            return SecretKey.StartsWith("sk_live_", StringComparison.Ordinal) || SecretKey.StartsWith("rk_live_", StringComparison.Ordinal)
                ? CheckoutMode.Live
                : CheckoutMode.Test;
        }
    }

    // The repository ships placeholders in place of every Stripe value.
    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
}
