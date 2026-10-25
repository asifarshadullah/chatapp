using Chat.Application.Interfaces;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Which models a deployment offers comes from the configuration it loads. The image ships only
/// the Groq option, so the local model cannot appear there, and a deploy without the Groq key
/// must fail at startup rather than on every message.
/// </summary>
public class ModelOptionsStartupTests
{
    private const string LocalMongo =
        "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";

    private static IModelCatalog Start(string environment, params (string Key, string Value)[] settings)
    {
        using var factory = new AuthApiFactory();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("MongoDB:ConnectionString", LocalMongo);
            builder.UseSetting("MongoDB:DatabaseName", "chatapp_test");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

        // Creating the client is what starts the host.
        app.CreateClient();
        return app.Services.GetRequiredService<IModelCatalog>();
    }

    private static readonly (string, string) GroqKey = ("Ai:Models:groq:ApiKey", "gsk_test_key");

    // ── What each environment offers ─────────────────────────────────────────

    [Fact]
    public void Production_WithoutTheGroqKey_RefusesToStart()
    {
        var act = () => Start("Production");

        act.Should().Throw<Exception>().WithMessage("*no model option is offered*");
    }

    [Fact]
    public void Production_WithTheGroqKey_OffersOnlyGroq()
    {
        var catalog = Start("Production", GroqKey);

        catalog.Offered.Select(o => o.Id).Should().Equal("groq");
        catalog.DefaultId.Should().Be("groq");
        catalog.IsOffered("local").Should().BeFalse();
    }

    [Fact]
    public void Development_WithoutAGroqKey_OffersOnlyTheLocalModel()
    {
        var catalog = Start("Development");

        catalog.Offered.Select(o => o.Id).Should().Equal("local");
        catalog.DefaultId.Should().Be("local");
    }

    [Fact]
    public void Development_WithAGroqKey_OffersBoth_AndDefaultsToTheLocalModel()
    {
        var catalog = Start("Development", GroqKey);

        catalog.Offered.Select(o => o.Id).Should().BeEquivalentTo("local", "groq");
        catalog.DefaultId.Should().Be("local");
    }

    // ── Configurations that cannot work ──────────────────────────────────────

    [Theory]
    [InlineData("missing")]
    [InlineData("groq")]
    public void ADefaultThatIsNotOffered_RefusesToStart(string defaultModel)
    {
        // In Development, groq has no key unless one is set.
        var act = () => Start("Development", ("Ai:DefaultModel", defaultModel));

        act.Should().Throw<Exception>().WithMessage("*Ai:DefaultModel*");
    }

    [Theory]
    [InlineData("Ai:Models:local:BaseUrl", "not a url")]
    [InlineData("Ai:Models:local:BaseUrl", "ftp://localhost:11434/v1")]
    [InlineData("Ai:Models:local:Model", "")]
    [InlineData("Ai:Models:local:ReasoningEffort", "fast")]
    public void AnUnusableOfferedOption_RefusesToStart_NamingIt(string key, string value)
    {
        var act = () => Start("Development", (key, value));

        act.Should().Throw<Exception>().WithMessage("*Ai:Models:local*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void ADailyMessageLimitThatIsNotPositive_RefusesToStart(string limit)
    {
        var act = () => Start("Development", ("Chat:DailyMessageLimit", limit));

        act.Should().Throw<Exception>().WithMessage("*Chat:DailyMessageLimit*");
    }

    [Theory]
    [InlineData("Ai:MaxHistoryMessages")]
    [InlineData("Ai:MaxReplyTokens")]
    public void ABoundThatIsNotPositive_RefusesToStart(string key)
    {
        var act = () => Start("Development", (key, "0"));

        act.Should().Throw<Exception>().WithMessage($"*{key}*");
    }
}
