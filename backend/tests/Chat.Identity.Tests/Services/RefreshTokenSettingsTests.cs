using Chat.Identity.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Chat.Identity.Tests.Services;

/// <summary>Refresh-token policy binds from the "RefreshToken" configuration section.</summary>
public class RefreshTokenSettingsTests
{
    private static RefreshTokenSettings Bind(params (string Key, string Value)[] values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v =>
                new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

        var settings = new RefreshTokenSettings();
        config.GetSection("RefreshToken").Bind(settings);
        return settings;
    }

    [Fact]
    public void Binds_ConfiguredValues()
    {
        var settings = Bind(("RefreshToken:LifetimeDays", "30"));

        settings.LifetimeDays.Should().Be(30);
        settings.Lifetime.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public void Defaults_WhenTheSectionIsAbsent()
    {
        var settings = Bind();

        settings.Lifetime.Should().Be(TimeSpan.FromDays(1));
    }

    // ── Task 1.1 — the remembered lifetime sits alongside the ordinary one ───

    [Fact]
    public void PersistentLifetime_DefaultsToThirtyDays()
    {
        var settings = Bind();

        settings.PersistentLifetime.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public void PersistentLifetime_BindsAConfiguredValue()
    {
        var settings = Bind(("RefreshToken:PersistentLifetimeDays", "90"));

        settings.PersistentLifetimeDays.Should().Be(90);
        settings.PersistentLifetime.Should().Be(TimeSpan.FromDays(90));
    }

    [Fact]
    public void TheTwoLifetimesAreIndependent()
    {
        var settings = Bind(
            ("RefreshToken:LifetimeDays", "2"),
            ("RefreshToken:PersistentLifetimeDays", "60"));

        settings.Lifetime.Should().Be(TimeSpan.FromDays(2));
        settings.PersistentLifetime.Should().Be(TimeSpan.FromDays(60));
    }
}
