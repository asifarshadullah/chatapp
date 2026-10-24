using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Identity.Application.Interfaces;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
    /// <summary>The repository every request uses, exposed so tests can count conversations.</summary>
    public CountingChatRepository Repository => Services.GetRequiredService<CountingChatRepository>();

    /// <summary>The fake AI model every request uses, exposed so tests can count its calls.</summary>
    public FakeAiProvider Ai => (FakeAiProvider)Services.GetRequiredService<IAiProvider>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace MongoDB repository with fast in-memory version
            var repoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatRepository));
            if (repoDescriptor is not null)
                services.Remove(repoDescriptor);
            services.AddSingleton(new CountingChatRepository(new InMemoryChatRepository()));
            services.AddSingleton<IChatRepository>(sp => sp.GetRequiredService<CountingChatRepository>());

            // Replace Ollama AI provider with a deterministic fake
            var aiDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiProvider));
            if (aiDescriptor is not null)
                services.Remove(aiDescriptor);
            services.AddSingleton<IAiProvider, FakeAiProvider>();

            // Authenticate every request as the caller named in the test headers.
            services.AddAuthentication("TestScheme")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "TestScheme", _ => { });

            // Allow every permission; Chat.Identity.Tests covers roles with the real service.
            var permSvcDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPermissionService));
            if (permSvcDescriptor is not null)
                services.Remove(permSvcDescriptor);
            services.AddSingleton<IPermissionService, AlwaysAllowPermissionService>();
        });
    }
}

/// <summary>
/// Permission service stub that grants every permission.
/// Used in ChatApiFactory to keep existing tests green under policy-based auth.
/// </summary>
public class AlwaysAllowPermissionService : IPermissionService
{
    public Task<bool> IsAuthorizedAsync(Guid userId, string permission, CancellationToken ct = default)
        => Task.FromResult(true);
}

/// <summary>
/// Deterministic AI provider for integration tests.
/// Returns a fixed response so tests are fast, offline, and predictable, and counts its calls
/// so a test can show a refused request never reached the model.
/// </summary>
public class FakeAiProvider : IAiProvider
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Replaces the fixed reply. Set it only on a test's own factory, not a shared fixture.</summary>
    public Func<CancellationToken, IAsyncEnumerable<string>>? Script { get; set; }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        if (Script is not null)
        {
            await foreach (var token in Script(ct).WithCancellation(ct))
                yield return token;
            yield break;
        }

        yield return "Fake";
        yield return " AI";
        yield return " response";
        await Task.Yield();
    }
}

/// <summary>
/// Test authentication handler that automatically authenticates every request.
/// The caller is the user named in the X-Test-User header, or a fixed test user when the
/// header is absent, with the role named in X-Test-Role, or "User". Ownership tests can then
/// act as different people, administrators included, without issuing real JWTs.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Request header that selects which user the request is signed in as.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>Request header that selects the caller's role.</summary>
    public const string RoleHeader = "X-Test-Role";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Guid.TryParse(Request.Headers[UserHeader], out var requested) ? requested : TestUserId;
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("email", "testuser@example.com"),
            new Claim(ClaimTypes.Role, Request.Headers.TryGetValue(RoleHeader, out var role) ? role.ToString() : "User")
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
