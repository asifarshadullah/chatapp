namespace Chat.Billing.Infrastructure.Configuration;

/// <summary>Whether users can upgrade, and whether Stripe takes real money when they do.</summary>
public enum CheckoutMode
{
    Off,
    Test,
    Live,
}
