using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chat.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Chat.Api.Tests.Hosting;

/// <summary>
/// The host polls the health endpoint, and the deploy workflow reads the commit from it to see
/// when the new image is live.
/// </summary>
public class HealthTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;

    public HealthTests(ChatApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_WithoutSigningIn_IsHealthy()
    {
        // The real app, with real JWT authentication, and no token sent.
        using var app = new WebApplicationFactory<Program>();
        var response = await app.CreateClient().GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task Health_NamesTheCommitTheAppWasBuiltFrom()
    {
        using var built = _factory.WithWebHostBuilder(b => b.UseSetting("APP_COMMIT", "0123abc"));

        var body = await built.CreateClient().GetFromJsonAsync<JsonElement>("/healthz");

        body.GetProperty("commit").GetString().Should().Be("0123abc");
    }

    [Fact]
    public async Task Health_NamesNoCommit_WhenNotBuiltFromOne()
    {
        var body = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/healthz");

        body.GetProperty("commit").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Health_NamesNoCommit_WhenTheImageWasBuiltWithoutOne()
    {
        // The Dockerfile sets APP_COMMIT from its build argument, which is empty when not passed.
        using var built = _factory.WithWebHostBuilder(b => b.UseSetting("APP_COMMIT", string.Empty));

        var body = await built.CreateClient().GetFromJsonAsync<JsonElement>("/healthz");

        body.GetProperty("commit").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
