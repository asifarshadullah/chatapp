using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Controllers;

/// <summary>GET and DELETE /billing/subscription: the user's current plan, and cancelling at the period's end.</summary>
public class BillingSubscriptionTests : IClassFixture<ChatApiFactory>
{
    private static readonly Guid User = TestAuthHandler.TestUserId;
    private static readonly DateTime PeriodEnd = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);

    private readonly ChatApiFactory _factory;
    private readonly InMemoryPlanRepository _plans = new();
    private readonly InMemorySubscriptionRepository _subscriptions = new();
    private readonly FakePaymentGateway _gateway = new();

    public BillingSubscriptionTests(ChatApiFactory factory) => _factory = factory;

    private HttpClient Client() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:SecretKey", "sk_test_abc");
            builder.UseSetting("Stripe:WebhookSecret", "whsec_abc");
            builder.UseSetting("Stripe:PriceIds:Pro", "price_pro");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPlanRepository>(_plans);
                services.AddSingleton<ISubscriptionRepository>(_subscriptions);
                services.AddSingleton<IPaymentGateway>(_gateway);
            });
        }).CreateClient();

    private record Current(string PlanName, string Tier, string? Status, DateTime? CurrentPeriodEnd, bool CancelAtPeriodEnd);

    private async Task OnPro(SubscriptionStatus status)
    {
        _gateway.Holds(new StripeSubscriptionSnapshot("sub_1", User, PlanTier.Pro, status, PeriodEnd, false));
        await _subscriptions.SaveAsync(new Subscription(Guid.NewGuid(), User, _plans.Pro.Id, status, PeriodEnd, "sub_1"));
    }

    [Fact]
    public async Task AUserWithoutASubscription_IsOnFree()
    {
        var current = await Client().GetFromJsonAsync<Current>("/billing/subscription");

        current.Should().Be(new Current("Free", "Free", null, null, false));
    }

    [Fact]
    public async Task AnActiveSubscription_IsPro_WithItsPeriodEnd()
    {
        await OnPro(SubscriptionStatus.Active);

        var current = await Client().GetFromJsonAsync<Current>("/billing/subscription");

        current.Should().Be(new Current("Pro", "Pro", "Active", PeriodEnd, false));
    }

    [Fact]
    public async Task APastDueSubscription_LeavesTheUserOnFree()
    {
        await OnPro(SubscriptionStatus.PastDue);

        var current = await Client().GetFromJsonAsync<Current>("/billing/subscription");

        current!.PlanName.Should().Be("Free");
        current.CurrentPeriodEnd.Should().BeNull();
    }

    [Fact]
    public async Task Cancelling_EndsProAtThePeriodsEnd_NotNow()
    {
        await OnPro(SubscriptionStatus.Active);
        var client = Client();

        var response = await client.DeleteAsync("/billing/subscription");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _gateway.CancelledAtPeriodEnd.Should().Equal("sub_1");
        var current = await client.GetFromJsonAsync<Current>("/billing/subscription");
        current.Should().Be(new Current("Pro", "Pro", "Active", PeriodEnd, true));
    }

    [Fact]
    public async Task CancellingOnFree_IsNotFound()
    {
        var response = await Client().DeleteAsync("/billing/subscription");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _gateway.CancelledAtPeriodEnd.Should().BeEmpty();
    }
}
