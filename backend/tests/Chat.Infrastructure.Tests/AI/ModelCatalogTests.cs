using Chat.Infrastructure.AI;
using Chat.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Tests.AI;

/// <summary>
/// What the app offers is decided by which options have a key, so the deployed image, which
/// defines Groq without one, offers nothing until the host supplies the key.
/// </summary>
public class ModelCatalogTests
{
    private static ModelCatalog Catalog() => new(Options.Create(new AiSettings
    {
        DefaultModel = "local",
        Models =
        {
            ["local"] = new() { Name = "Local · gemma3:4b", BaseUrl = "http://localhost:11434/v1", ApiKey = "ollama", Model = "gemma3:4b" },
            ["groq"] = new() { Name = "Groq · gpt-oss-120b", BaseUrl = "https://api.groq.com/openai/v1", ApiKey = "", Model = "openai/gpt-oss-120b" },
        },
    }));

    [Fact]
    public void OnlyOptionsWithAKey_AreOffered()
    {
        Catalog().Offered.Select(o => o.Id).Should().Equal("local");
    }

    [Fact]
    public void TheOfferedOptions_ExposeOnlyTheirIdAndName()
    {
        var catalog = Catalog();

        catalog.DefaultId.Should().Be("local");
        catalog.Offered.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = "local", Name = "Local · gemma3:4b" });
        typeof(Chat.Application.DTOs.ModelSummary).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Id", "Name");
    }

    [Theory]
    [InlineData("local", true)]
    [InlineData("LOCAL", true)]
    [InlineData("groq", false)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    public void IsOffered_IsTrueOnlyForAnOptionWithAKey(string id, bool offered)
    {
        Catalog().IsOffered(id).Should().Be(offered);
    }
}
