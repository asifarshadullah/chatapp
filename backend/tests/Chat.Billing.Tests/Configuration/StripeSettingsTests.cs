using Chat.Billing.Infrastructure.Configuration;
using FluentAssertions;

namespace Chat.Billing.Tests.Configuration;

/// <summary>Checkout is offered only with a complete Stripe setup, and says whether it is a test.</summary>
public class StripeSettingsTests
{
    private static StripeSettings Settings(string secretKey, string webhookSecret, string? proPrice) => new()
    {
        SecretKey = secretKey,
        WebhookSecret = webhookSecret,
        PriceIds = proPrice is null ? new() : new() { ["Pro"] = proPrice },
    };

    [Fact]
    public void ACompleteTestSetup_IsTestMode() =>
        Settings("sk_test_abc", "whsec_abc", "price_abc").Mode.Should().Be(CheckoutMode.Test);

    [Theory]
    [InlineData("sk_test_abc", "", "price_abc")]
    [InlineData("sk_test_abc", "whsec_abc", "")]
    [InlineData("sk_test_abc", "whsec_abc", null)]
    [InlineData("", "whsec_abc", "price_abc")]
    [InlineData("sk_test_placeholder", "whsec_placeholder", "price_placeholder_pro")]
    [InlineData("sk_test_abc", "whsec_placeholder", "price_abc")]
    public void AnIncompleteSetup_IsOff(string secretKey, string webhookSecret, string? proPrice) =>
        Settings(secretKey, webhookSecret, proPrice).Mode.Should().Be(CheckoutMode.Off);

    [Theory]
    [InlineData("sk_live_abc")]
    [InlineData("rk_live_abc")]
    public void ALiveKey_IsLiveMode(string secretKey) =>
        Settings(secretKey, "whsec_abc", "price_abc").Mode.Should().Be(CheckoutMode.Live);

    [Fact]
    public void TheDefaults_AreOff() => new StripeSettings().Mode.Should().Be(CheckoutMode.Off);
}
