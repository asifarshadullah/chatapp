using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that replaces MongoDB with InMemoryChatRepository,
/// Ollama with FakeAiProvider, and JWT validation with TestAuthHandler so all
/// API integration tests run without any external dependencies.
/// </summary>
public class ChatApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace MongoDB repository with fast in-memory version
            var repoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatRepository));
            if (repoDescriptor is not null)
                services.Remove(repoDescriptor);
            services.AddSingleton<IChatRepository, InMemoryChatRepository>();

            // Replace Ollama AI provider with a deterministic fake
            var aiDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiProvider));
            if (aiDescriptor is not null)
                services.Remove(aiDescriptor);
            services.AddSingleton<IAiProvider, FakeAiProvider>();

            // Replace JWT validation with a test handler that always authenticates.
            // This keeps all 42+ existing tests green after [Authorize] was added to
            // ChatController and ChatHub.
            services.AddAuthentication("TestScheme")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "TestScheme", _ => { });
        });
    }
}

/// <summary>
/// Deterministic AI provider for integration tests.
/// Returns a fixed response so tests are fast, offline, and predictable.
/// </summary>
public class FakeAiProvider : IAiProvider
{
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "Fake";
        yield return " AI";
        yield return " response";
        await Task.Yield();
    }
}

/// <summary>
/// Test authentication handler that automatically authenticates every request
/// with a fixed test-user identity. Eliminates the need to issue real JWTs in
/// ChatApiFactory-backed integration tests.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("email", "testuser@example.com"),
            new Claim(ClaimTypes.Role, "User")
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
