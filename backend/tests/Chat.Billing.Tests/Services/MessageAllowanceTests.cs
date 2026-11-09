using Chat.Billing.Application.DTOs;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Services;
using FluentAssertions;

namespace Chat.Billing.Tests.Services;

/// <summary>A user's daily message limit is their plan's, and Free users learn what Pro allows.</summary>
public class MessageAllowanceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Plan Free = new(Guid.NewGuid(), "Free", PlanTier.Free, 0m, 10, [Feature.Chat]);
    private static readonly Plan Pro = new(Guid.NewGuid(), "Pro", PlanTier.Pro, 19m, 100, [Feature.Chat]);

    private static async Task<MessageAllowance> AllowanceWith(SubscriptionStatus? status)
    {
        var subscriptions = new FakeSubscriptionRepository();
        if (status is { } s)
            await subscriptions.SaveAsync(new Subscription(Guid.NewGuid(), UserId, Pro.Id, s, DateTime.UtcNow.AddDays(30), "sub_1"));
        return await new RealPlanFeatureService(subscriptions, new FakePlanRepository(Free, Pro)).GetMessageAllowanceAsync(UserId);
    }

    [Fact]
    public async Task WithoutASubscription_TheUserHasFreesLimit_AndCouldHavePros()
    {
        (await AllowanceWith(null)).Should().Be(new MessageAllowance(10, 100));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Trialing)]
    public async Task OnPro_TheUserHasProsLimit_AndNothingToUpgradeTo(SubscriptionStatus status)
    {
        (await AllowanceWith(status)).Should().Be(new MessageAllowance(100, null));
    }

    [Fact]
    public async Task OnProEndingAtThePeriodsEnd_TheUserStillHasProsLimit()
    {
        var subscriptions = new FakeSubscriptionRepository();
        await subscriptions.SaveAsync(new Subscription(Guid.NewGuid(), UserId, Pro.Id, SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(30), "sub_1", cancelAtPeriodEnd: true));

        var allowance = await new RealPlanFeatureService(subscriptions, new FakePlanRepository(Free, Pro)).GetMessageAllowanceAsync(UserId);

        allowance.Should().Be(new MessageAllowance(100, null));
    }

    [Theory]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task WithASubscriptionThatGrantsNothing_TheUserIsBackOnFree(SubscriptionStatus status)
    {
        (await AllowanceWith(status)).Should().Be(new MessageAllowance(10, 100));
    }
}
