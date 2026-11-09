using System.Net;
using System.Security.Cryptography;
using System.Text;
using Chat.Api.Tests.Infrastructure;
using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Controllers;

/// <summary>
/// POST /webhooks/stripe: every event about a subscription makes the app read that subscription
/// from Stripe and store it, so order and repeats do not matter. Payloads are signed here the way
/// Stripe signs them.
/// </summary>
public class StripeWebhookTests : IClassFixture<ChatApiFactory>
{
    private const string WebhookSecret = "whsec_test_secret";
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime PeriodEnd = new(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly ChatApiFactory _factory;
    private readonly InMemoryPlanRepository _plans = new();
    private readonly InMemorySubscriptionRepository _subscriptions = new();
    private readonly FakePaymentGateway _gateway = new();

    public StripeWebhookTests(ChatApiFactory factory) => _factory = factory;

    private HttpClient Client() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:SecretKey", "sk_test_abc");
            builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
            builder.UseSetting("Stripe:PriceIds:Pro", "price_pro");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPlanRepository>(_plans);
                services.AddSingleton<ISubscriptionRepository>(_subscriptions);
                services.AddSingleton<IPaymentGateway>(_gateway);
            });
        }).CreateClient();

    private static string Event(string type, string objectJson) =>
        $$"""{"id":"evt_{{Guid.NewGuid():N}}","object":"event","api_version":"2024-04-10","created":1767225600,"livemode":false,"pending_webhooks":1,"request":{"id":null,"idempotency_key":null},"type":"{{type}}","data":{"object":""" + objectJson + "}}";

    private static string Signature(string payload, string secret = WebhookSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
            request.Headers.Add("Stripe-Signature", signature);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostSigned(HttpClient client, string payload) => Post(client, payload, Signature(payload));

    private void StripeHolds(string id, SubscriptionStatus status) =>
        _gateway.Holds(new StripeSubscriptionSnapshot(id, User, PlanTier.Pro, status, PeriodEnd, false));

    public static TheoryData<string, string> SubscriptionEvents => new()
    {
        { "checkout.session.completed", """{"id":"cs_1","object":"checkout.session","subscription":"sub_1"}""" },
        { "customer.subscription.created", """{"id":"sub_1","object":"subscription"}""" },
        { "customer.subscription.updated", """{"id":"sub_1","object":"subscription"}""" },
        { "customer.subscription.deleted", """{"id":"sub_1","object":"subscription"}""" },
        { "invoice.paid", """{"id":"in_1","object":"invoice","subscription":"sub_1"}""" },
        { "invoice.payment_succeeded", """{"id":"in_1","object":"invoice","subscription":"sub_1"}""" },
        { "invoice.payment_failed", """{"id":"in_1","object":"invoice","subscription":"sub_1"}""" },
    };

    [Theory]
    [MemberData(nameof(SubscriptionEvents))]
    public async Task EachSubscriptionEvent_ReadsThatSubscriptionFromStripe_AndStoresIt(string type, string objectJson)
    {
        StripeHolds("sub_1", SubscriptionStatus.Active);

        var response = await PostSigned(Client(), Event(type, objectJson));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _gateway.Reads.Should().Equal("sub_1");
        (await _subscriptions.GetByUserIdAsync(User))!.StripeSubscriptionId.Should().Be("sub_1");
    }

    [Fact]
    public async Task APaidInvoiceBeforeTheCompletedCheckout_StillLeavesTheUserOnPro()
    {
        StripeHolds("sub_1", SubscriptionStatus.Active);
        var client = Client();

        await PostSigned(client, Event("invoice.paid", """{"id":"in_1","object":"invoice","subscription":"sub_1"}"""));
        await PostSigned(client, Event("checkout.session.completed", """{"id":"cs_1","object":"checkout.session","subscription":"sub_1"}"""));

        var stored = await _subscriptions.GetByUserIdAsync(User);
        stored!.GrantsPlan.Should().BeTrue();
        stored.PlanId.Should().Be(_plans.Pro.Id);
    }

    [Theory]
    [InlineData("wrong_secret")]
    [InlineData(null)]
    public async Task AnEventWithoutAValidSignature_IsRefused_AndChangesNothing(string? secret)
    {
        StripeHolds("sub_1", SubscriptionStatus.Active);
        var payload = Event("customer.subscription.updated", """{"id":"sub_1","object":"subscription"}""");

        var response = await Post(Client(), payload, secret is null ? null : Signature(payload, secret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _gateway.Reads.Should().BeEmpty();
        (await _subscriptions.GetByUserIdAsync(User)).Should().BeNull();
    }

    [Fact]
    public async Task AnEventOfAnotherType_IsAcknowledged_AndChangesNothing()
    {
        var response = await PostSigned(Client(), Event("customer.created", """{"id":"cus_1","object":"customer"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _gateway.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEventAboutASubscriptionStripeNoLongerKnows_IsAcknowledged()
    {
        var response = await PostSigned(Client(), Event("customer.subscription.deleted", """{"id":"sub_gone","object":"subscription"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _subscriptions.GetByUserIdAsync(User)).Should().BeNull();
    }

    [Fact]
    public async Task WhenStripeCannotBeReached_TheEventFails_SoStripeSendsItAgain()
    {
        _gateway.ReadFailure = new PaymentGatewayException("timeout", new TimeoutException());

        var response = await PostSigned(Client(), Event("customer.subscription.updated", """{"id":"sub_1","object":"subscription"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await _subscriptions.GetByUserIdAsync(User)).Should().BeNull();
    }
}
