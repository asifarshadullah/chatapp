using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Api.Tests.Hosting;

/// <summary>
/// The deployed app serves the built UI and the API from one origin. A link into the app must
/// load it, while a mistyped API call must still fail as an API call.
/// </summary>
public class SameOriginUiTests : IClassFixture<BuiltUiApiFactory>
{
    private readonly HttpClient _client;

    public SameOriginUiTests(BuiltUiApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/billing/success")]
    public async Task APageOfTheApp_ReturnsTheEntryPage_Uncached(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(BuiltUiApiFactory.EntryPage);
        response.Headers.CacheControl?.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task ABuiltAsset_IsServed_AndCachedAsImmutable()
    {
        var response = await _client.GetAsync(BuiltUiApiFactory.AssetPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(BuiltUiApiFactory.AssetContent);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.CacheControl.Extensions.Should().Contain(e => e.Name == "immutable");
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/auth/nope")]
    public async Task AnUnknownApiPath_IsNotFound_AndNotTheEntryPage(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("chat-ui entry page");
    }

    [Fact]
    public async Task AnExistingApiRoute_IsNotShadowedByTheFallbacks()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/messages", new { message = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
