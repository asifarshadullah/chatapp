using Chat.Billing.Infrastructure.Configuration;
using FluentAssertions;

namespace Chat.Billing.Tests.Configuration;

public class BillingSettingsTests
{
    [Fact]
    public void TheDefaults_AreFreeAtTenADayAndProAtAHundred()
    {
        var plans = new BillingSettings().Plans;

        plans.Free.Should().BeEquivalentTo(new PlanSettings { Name = "Free", PricePerMonth = 0m, DailyMessages = 10 });
        plans.Pro.Should().BeEquivalentTo(new PlanSettings { Name = "Pro", PricePerMonth = 19m, DailyMessages = 100 });
    }
}
