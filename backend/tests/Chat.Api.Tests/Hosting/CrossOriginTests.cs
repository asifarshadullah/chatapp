using Chat.Api.Tests.Infrastructure;
using FluentAssertions;

namespace Chat.Api.Tests.Hosting;

/// <summary>
/// The UI reaches the API on its own origin, so no other origin is granted access, not even
/// the Vite dev server's: on the live API that would trust a page on any visitor's machine.
/// </summary>
public class CrossOriginTests : IClassFixture<ChatApiFactory>
{
    private const string OtherOrigin = "http://localhost:5173";

    private readonly HttpClient _client;

    public CrossOriginTests(ChatApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task APreflightFromAnotherOrigin_IsNotGrantedAccess()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/auth/refresh");
        request.Headers.Add("Origin", OtherOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task ARequestFromAnotherOrigin_IsNotGrantedAccess()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/{Guid.NewGuid()}/history");
        request.Headers.Add("Origin", OtherOrigin);

        var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
