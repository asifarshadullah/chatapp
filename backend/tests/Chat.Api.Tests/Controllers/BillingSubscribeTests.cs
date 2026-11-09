using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Controllers;

/// <summary>
/// Starting an upgrade: refused plainly when Stripe is not set up, and otherwise sent to Stripe
/// only for a Free user choosing Pro.
/// </summary>
public class BillingSubscribeTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;
    private readonly InMemoryPlanRepository _plans = new();
    private readonly InMemorySubscriptionRepository _subscriptions = new();
    private readonly FakePaymentGateway _gateway = new();

    public BillingSubscribeTests(ChatApiFactory factory) => _factory = factory;

    private static readonly (string, string)[] TestSetup =
        [("Stripe:SecretKey", "sk_test_abc"), ("Stripe:WebhookSecret", "whsec_abc"), ("Stripe:PriceIds:Pro", "price_pro")];

    private static readonly (string, string)[] NoSetup =
        [("Stripe:SecretKey", "sk_test_placeholder"), ("Stripe:WebhookSecret", "whsec_placeholder"), ("Stripe:PriceIds:Pro", "price_placeholder_pro")];

    private HttpClient Client(params (string Key, string Value)[] settings) =>
        _factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPlanRepository>(_plans);
                services.AddSingleton<ISubscriptionRepository>(_subscriptions);
                services.AddSingleton<IPaymentGateway>(_gateway);
            });
        }).CreateClient();

    private static Task<HttpResponseMessage> Subscribe(HttpClient client, Guid planId) =>
        client.PostAsJsonAsync("/billing/subscribe", new { planId });

    private record ModeResponse(string Mode);

    private record CheckoutResponse(string CheckoutUrl, string SessionId);

    // ── The mode ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, "off")]
    [InlineData(true, "test")]
    public async Task TheCheckoutMode_IsReported(bool configured, string mode)
    {
        var client = Client(configured ? TestSetup : NoSetup);

        var response = await client.GetFromJsonAsync<ModeResponse>("/billing/checkout-mode");

        response!.Mode.Should().Be(mode);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithCheckoutOff_SubscribeSaysUpgradesAreOff_WithoutReadingPlansOrCallingStripe()
    {
        var response = await Subscribe(Client(NoSetup), _plans.Pro.Id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("Upgrades are turned off on this demo. No payment was taken.");
        _plans.Reads.Should().Be(0);
        _gateway.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownPlan_IsNotFound()
    {
        var response = await Subscribe(Client(TestSetup), Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _gateway.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Free_NeedsNoCheckout()
    {
        var response = await Subscribe(Client(TestSetup), _plans.Free.Id);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _gateway.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Trialing)]
    public async Task AUserAlreadyOnPro_CannotPayTwice(SubscriptionStatus status)
    {
        await _subscriptions.SaveAsync(new Subscription(Guid.NewGuid(), TestAuthHandler.TestUserId, _plans.Pro.Id, status,
            DateTime.UtcNow.AddDays(20), "sub_existing"));

        var response = await Subscribe(Client(TestSetup), _plans.Pro.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _gateway.Calls.Should().Be(0);
    }

    [Fact]
    public async Task WhenStripeRefusesTheCheckout_TheUserGetsAPlainFailure()
    {
        _gateway.FailWith = new PaymentGatewayException("No such price: 'price_pro'", new Exception());

        var response = await Subscribe(Client(TestSetup), _plans.Pro.Id);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Be("The upgrade could not be started. Please try again later.");
    }

    // ── Starting a checkout ──────────────────────────────────────────────────

    [Fact]
    public async Task AFreeUserChoosingPro_IsSentToStripe_WithTheirIdAndTheReturnAddresses()
    {
        var response = await Subscribe(Client(TestSetup), _plans.Pro.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<CheckoutResponse>())!.CheckoutUrl.Should().Be("https://checkout.stripe.com/c/pay/cs_test_1");
        _gateway.LastCheckout.Should().Be((TestAuthHandler.TestUserId, "price_pro",
            "http://localhost/billing/success", "http://localhost/billing/cancel"));
    }

    [Fact]
    public async Task AFreeUserWhosePastSubscriptionEnded_CanSubscribeAgain()
    {
        await _subscriptions.SaveAsync(new Subscription(Guid.NewGuid(), TestAuthHandler.TestUserId, _plans.Pro.Id,
            SubscriptionStatus.Cancelled, DateTime.UtcNow.AddDays(-1), "sub_old"));

        var response = await Subscribe(Client(TestSetup), _plans.Pro.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
