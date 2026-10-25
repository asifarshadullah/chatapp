using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// A deployed app signing tokens with a secret anyone can read lets anyone forge a session,
/// so outside Development the host refuses to start without a proper one.
/// </summary>
public class JwtSecretStartupTests
{
    private const string OldPlaceholder = "REPLACE-WITH-A-SECURE-256-BIT-SECRET-IN-PRODUCTION";
    private const string ProperSecret = "a-proper-secret-that-is-comfortably-longer-than-thirty-two-bytes-0123";

    // Production does not load appsettings.Development.json, so the database has to be named here.
    private const string LocalMongo =
        "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";

    private static void Start(string environment, string? secret)
    {
        using var factory = new AuthApiFactory();
        using var scoped = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("MongoDB:ConnectionString", LocalMongo);
            builder.UseSetting("MongoDB:DatabaseName", "chatapp_test");
            // UseSetting cannot unset a value, and an empty one reads the same as a missing one.
            builder.UseSetting("Jwt:Secret", secret ?? string.Empty);
        });

        // Creating the client is what starts the host.
        scoped.CreateClient();
    }

    [Fact]
    public void ThePlaceholderRefusesToStartInProduction()
    {
        var act = () => Start("Production", OldPlaceholder);

        act.Should().Throw<Exception>().WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void AShortSecretRefusesToStartInProduction()
    {
        var act = () => Start("Production", new string('x', 31));

        act.Should().Throw<Exception>().WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void AMissingSecretRefusesToStartInProduction()
    {
        var act = () => Start("Production", null);

        act.Should().Throw<Exception>().WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void AProperSecretStartsInProduction()
    {
        var act = () => Start("Production", ProperSecret);

        act.Should().NotThrow();
    }

    [Fact]
    public void DevelopmentStartsWithItsOwnSettings()
    {
        var act = () => Start("Development", "dev-secret-key-change-this-in-production-min-32-chars");

        act.Should().NotThrow();
    }
}
