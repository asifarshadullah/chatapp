using System.Net;
using System.Text.Json;
using Chat.Api.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Api.Tests.Controllers;

/// <summary>
/// The Settings dialog lists what this deployment offers. It must see names only: a base URL,
/// a model service or a key would tell any signed-in user how the app reaches its models.
/// </summary>
public class ModelsEndpointTests : IClassFixture<ChatApiFactory>
{
    private readonly HttpClient _client;

    public ModelsEndpointTests(ChatApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetModels_ListsTheOfferedOptionsAndTheDefault()
    {
        var response = await _client.GetAsync("/api/chat/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("defaultModel").GetString().Should().Be("local");
        body.GetProperty("models").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString(), m.GetProperty("name").GetString()))
            .Should().Equal(("local", "Local · gemma3:4b"));
    }

    [Fact]
    public async Task GetModels_RevealsNoAddressModelOrKey()
    {
        var body = await _client.GetStringAsync("/api/chat/models");

        body.Should().NotContain("11434").And.NotContain("http").And.NotContain("\"ollama\"")
            .And.NotContain("apiKey").And.NotContain("baseUrl");
    }
}
