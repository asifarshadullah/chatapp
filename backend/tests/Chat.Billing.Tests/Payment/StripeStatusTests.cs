using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Payment;
using FluentAssertions;

namespace Chat.Billing.Tests.Payment;

/// <summary>Stripe's statuses map to the four the app keeps (design.md, decision 4).</summary>
public class StripeStatusTests
{
    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.PastDue)]
    [InlineData("incomplete", SubscriptionStatus.PastDue)]
    [InlineData("paused", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Cancelled)]
    [InlineData("incomplete_expired", SubscriptionStatus.Cancelled)]
    public void AStripeStatus_MapsToTheStoredOne(string stripeStatus, SubscriptionStatus expected) =>
        StripePaymentGateway.ToStatus(stripeStatus).Should().Be(expected);

    [Fact]
    public void AStatusStripeAddsLater_GrantsNothing() =>
        StripePaymentGateway.ToStatus("something_new").Should().Be(SubscriptionStatus.PastDue);
}
