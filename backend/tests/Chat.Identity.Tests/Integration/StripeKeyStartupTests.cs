using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// The demo runs on Stripe's test mode, so a live key, which takes real money, must be asked for
/// explicitly rather than slip in through a misplaced secret.
/// </summary>
public class StripeKeyStartupTests
{
    private const string LocalMongo =
        "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";

    private static void Start(params (string Key, string Value)[] settings)
    {
        using var factory = new AuthApiFactory();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("MongoDB:ConnectionString", LocalMongo);
            builder.UseSetting("MongoDB:DatabaseName", "chatapp_test");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

        // Creating the client is what starts the host.
        app.CreateClient();
    }

    private static readonly (string, string)[] LiveSetup =
        [("Stripe:SecretKey", "sk_live_abc"), ("Stripe:WebhookSecret", "whsec_abc"), ("Stripe:PriceIds:Pro", "price_abc")];

    [Fact]
    public void ALiveKey_WithoutTheOptIn_RefusesToStart()
    {
        var act = () => Start(LiveSetup);

        act.Should().Throw<Exception>().WithMessage("*Stripe:AllowLiveKey*");
    }

    [Fact]
    public void ALiveKey_WithTheOptIn_Starts()
    {
        var act = () => Start([.. LiveSetup, ("Stripe:AllowLiveKey", "true")]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ATestKey_Starts()
    {
        var act = () => Start(("Stripe:SecretKey", "sk_test_abc"), ("Stripe:WebhookSecret", "whsec_abc"), ("Stripe:PriceIds:Pro", "price_abc"));

        act.Should().NotThrow();
    }

    [Fact]
    public void NoKey_Starts()
    {
        var act = () => Start(("Stripe:SecretKey", ""));

        act.Should().NotThrow();
    }
}
