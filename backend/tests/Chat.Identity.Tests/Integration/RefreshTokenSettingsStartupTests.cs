using Chat.Identity.Application.Interfaces;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Task 1.2 — two lifetimes are two ways to get it wrong. A remembered session that is
/// shorter than an ordinary one is a misconfiguration that would quietly make the
/// "keep me signed in" choice harmful, so the host refuses to start on it.
/// </summary>
public class RefreshTokenSettingsStartupTests
{
    private static IRefreshTokenSettings Resolve(string lifetimeDays, string persistentDays)
    {
        using var factory = new AuthApiFactory();
        using var scoped = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RefreshToken:LifetimeDays", lifetimeDays);
            builder.UseSetting("RefreshToken:PersistentLifetimeDays", persistentDays);
        });

        // Creating the client is what starts the host.
        scoped.CreateClient();
        return scoped.Services.GetRequiredService<IRefreshTokenSettings>();
    }

    [Fact]
    public void AValidConfigurationStarts()
    {
        var settings = Resolve("1", "30");

        settings.Lifetime.Should().Be(TimeSpan.FromDays(1));
        settings.PersistentLifetime.Should().Be(TimeSpan.FromDays(30));
    }

    [Theory]
    [InlineData("30", "30")]
    [InlineData("30", "7")]
    public void ARememberedLifetimeThatIsNotLongerRefusesToStart(string lifetime, string persistent)
    {
        var act = () => Resolve(lifetime, persistent);

        act.Should().Throw<Exception>()
            .WithMessage("*PersistentLifetimeDays*");
    }

    [Theory]
    [InlineData("0", "30")]
    [InlineData("-1", "30")]
    public void ANonPositiveLifetimeRefusesToStart(string lifetime, string persistent)
    {
        var act = () => Resolve(lifetime, persistent);

        act.Should().Throw<Exception>();
    }
}
