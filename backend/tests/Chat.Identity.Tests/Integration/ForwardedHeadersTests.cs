using System.Net;
using Chat.Identity.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Behind the host's TLS proxy the app sees plain HTTP. The image turns on ASP.NET's
/// forwarded-headers switch by this name, so URLs the app builds keep the public HTTPS address.
/// </summary>
public class ForwardedHeadersTests
{
    private static async Task<string> GoogleRedirectUri(bool forwardedHeadersEnabled)
    {
        using var factory = new AuthApiFactory();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            if (forwardedHeadersEnabled)
                builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
            builder.UseSetting("Google:ClientId", "test-client-id");
            builder.UseSetting("Google:ClientSecret", "test-client-secret");
        });
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/google");
        request.Headers.Host = "live.example";
        request.Headers.Add("X-Forwarded-Proto", "https");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var query = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        return query["redirect_uri"]!;
    }

    [Fact]
    public async Task AForwardedHttpsRequest_SendsGoogleThePublicHttpsCallback()
    {
        var redirectUri = await GoogleRedirectUri(forwardedHeadersEnabled: true);

        redirectUri.Should().Be("https://live.example/signin-google");
    }

    [Fact]
    public async Task WithoutTheSwitch_TheForwardedSchemeIsIgnored()
    {
        var redirectUri = await GoogleRedirectUri(forwardedHeadersEnabled: false);

        redirectUri.Should().Be("http://live.example/signin-google");
    }
}
