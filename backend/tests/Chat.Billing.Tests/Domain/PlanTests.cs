using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using FluentAssertions;

namespace Chat.Billing.Tests.Domain;

public class PlanTests
{
    [Fact]
    public void APlan_CarriesItsDailyMessageLimit()
    {
        var plan = new Plan(Guid.NewGuid(), "Pro", PlanTier.Pro, 19m, 100, [Feature.Chat]);

        plan.DailyMessageLimit.Should().Be(100);
    }
}
