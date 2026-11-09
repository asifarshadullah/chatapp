using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using FluentAssertions;

namespace Chat.Billing.Tests.Domain;

public class SubscriptionTests
{
    private static Subscription With(SubscriptionStatus status, bool cancelAtPeriodEnd = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, DateTime.UtcNow, "sub_1", cancelAtPeriodEnd);

    [Theory]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.Trialing, true)]
    [InlineData(SubscriptionStatus.PastDue, false)]
    [InlineData(SubscriptionStatus.Cancelled, false)]
    public void OnlyAnActiveOrTrialingSubscription_GrantsItsPlan(SubscriptionStatus status, bool grants) =>
        With(status).GrantsPlan.Should().Be(grants);

    [Fact]
    public void ASubscriptionEndingAtThePeriodsEnd_StillGrantsItsPlanUntilThen()
    {
        var subscription = With(SubscriptionStatus.Active, cancelAtPeriodEnd: true);

        subscription.GrantsPlan.Should().BeTrue();
        subscription.CancelAtPeriodEnd.Should().BeTrue();
    }
}
