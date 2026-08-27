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
    private static IRefreshTokenSettings Resolve(string lifetimeDays, string persistentDays,
        string? graceWindowSeconds = null)
    {
        using var factory = new AuthApiFactory();
        using var scoped = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RefreshToken:LifetimeDays", lifetimeDays);
            builder.UseSetting("RefreshToken:PersistentLifetimeDays", persistentDays);
            if (graceWindowSeconds is not null)
                builder.UseSetting("RefreshToken:GraceWindowSeconds", graceWindowSeconds);
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

    // ── Task 6.2 — the grace window is a third way to get it wrong ──────────

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ANonPositiveGraceWindowRefusesToStart(string graceWindow)
    {
        var act = () => Resolve("1", "30", graceWindow);

        act.Should().Throw<Exception>().WithMessage("*GraceWindowSeconds*");
    }

    [Fact]
    public void AGraceWindowAsLongAsTheSessionRefusesToStart()
    {
        // A window that reaches the session's own length would stop replay detection from
        // ever firing: every consumed credential would still be inside it.
        var act = () => Resolve("1", "30", $"{TimeSpan.FromDays(1).TotalSeconds}");

        act.Should().Throw<Exception>().WithMessage("*GraceWindowSeconds*");
    }

    [Fact]
    public void AConfiguredGraceWindowStarts()
    {
        var settings = Resolve("1", "30", "5");

        settings.GraceWindow.Should().Be(TimeSpan.FromSeconds(5));
    }
}
