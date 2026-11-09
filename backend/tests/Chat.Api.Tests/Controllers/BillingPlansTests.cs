using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace Chat.Api.Tests.Controllers;

/// <summary>GET /billing/plans lists the plans written at startup, Free first.</summary>
public class BillingPlansTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;

    public BillingPlansTests(ChatApiFactory factory) => _factory = factory;

    private record PlanEntry(Guid Id, string Name, string Tier, decimal PricePerMonth, int DailyMessageLimit);

    [Fact]
    public async Task ThePlans_AreFreeThenPro_EachWithItsPriceAndDailyLimit()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            // Its own database, so the plans written here are not the developer's.
            builder.UseSetting("MongoDB:DatabaseName", "chatapp_api_plans_test");
            builder.UseSetting("Billing:Plans:Free:DailyMessages", "10");
            builder.UseSetting("Billing:Plans:Pro:DailyMessages", "100");
        }).CreateClient();

        var response = await client.GetAsync("/billing/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await response.Content.ReadFromJsonAsync<List<PlanEntry>>();
        plans!.Select(p => (p.Name, p.Tier, p.PricePerMonth, p.DailyMessageLimit)).Should().Equal(
            ("Free", "Free", 0m, 10),
            ("Pro", "Pro", 19m, 100));
        plans.Should().OnlyContain(p => p.Id != Guid.Empty);
    }
}
