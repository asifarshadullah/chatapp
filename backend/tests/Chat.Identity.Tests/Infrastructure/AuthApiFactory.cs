using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using Chat.Application.Interfaces;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Chat.Identity.Tests.Services;

namespace Chat.Identity.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory for Chat.Identity integration tests.
/// Replaces external dependencies (MongoDB, Ollama, real IdentityService) with fakes.
/// Uses a known JWT secret so tests can create valid tokens for [Authorize] endpoints.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "test-secret-key-that-is-long-enough-for-hmac-sha256-signing";
    public const string TestIssuer = "chatapp-test";
    public const string TestAudience = "chatapp-test";
    public static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override JWT settings so the middleware validates tokens we create in tests
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("Jwt:Issuer", TestIssuer);
        builder.UseSetting("Jwt:Audience", TestAudience);
        builder.UseSetting("Jwt:ExpiryMinutes", "60");

        builder.ConfigureServices(services =>
        {
            // Replace MongoDB chat repository with in-memory
            var repoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatRepository));
            if (repoDescriptor is not null) services.Remove(repoDescriptor);
            services.AddSingleton<IChatRepository, InMemoryChatRepository>();

            // Replace Ollama AI provider with fake
            var aiDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiProvider));
            if (aiDescriptor is not null) services.Remove(aiDescriptor);
            services.AddSingleton<IAiProvider, FakeAiProvider>();

            // Replace real IdentityService with controllable fake
            var idDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IIdentityService));
            if (idDescriptor is not null) services.Remove(idDescriptor);
            services.AddSingleton<IIdentityService, FakeIdentityService>();

            // Replace MongoRoleStore with in-memory FakeRoleStore seeded with default roles.
            // Real PermissionService runs against this — no MongoDB needed for RBAC tests.
            var roleStoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRoleStore));
            if (roleStoreDescriptor is not null) services.Remove(roleStoreDescriptor);
            var fakeRoleStore = new FakeRoleStore();
            fakeRoleStore.Roles.Add(new RoleInfo("User",     ["conversation:create", "conversation:read"]));
            fakeRoleStore.Roles.Add(new RoleInfo("OrgAdmin", ["conversation:create", "conversation:read", "conversation:share", "user:invite"]));
            fakeRoleStore.Roles.Add(new RoleInfo("Admin",    ["*"]));
            services.AddSingleton<IRoleStore>(fakeRoleStore);

            // Replace MongoDB-backed billing repos with stubs (no MongoDB needed for identity tests)
            var subRepoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISubscriptionRepository));
            if (subRepoDescriptor is not null) services.Remove(subRepoDescriptor);
            services.AddSingleton<ISubscriptionRepository, StubSubscriptionRepository>();

            var planRepoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPlanRepository));
            if (planRepoDescriptor is not null) services.Remove(planRepoDescriptor);
            services.AddSingleton<IPlanRepository, StubPlanRepository>();

            // Replace RealPlanFeatureService with a stub that always returns true
            var featureDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPlanFeatureService));
            if (featureDescriptor is not null) services.Remove(featureDescriptor);
            services.AddSingleton<IPlanFeatureService, AlwaysEnabledFeatureService>();
        });
    }

    /// <summary>Creates an HttpClient that sends a valid JWT for the test user (role: User).</summary>
    public HttpClient CreateAuthenticatedClient(Guid? userId = null)
    {
        var token = CreateToken(userId ?? TestUserId);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates an HttpClient with a JWT for the given user and role.</summary>
    public HttpClient CreateAuthenticatedClientWithRole(Guid userId, string role)
    {
        var token = CreateTokenWithRole(userId, role);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates a real, validly-signed JWT for the given user ID (role: User).</summary>
    public string CreateToken(Guid userId) => CreateTokenWithRole(userId, "User");

    /// <summary>Creates a real, validly-signed JWT for the given user ID and role.</summary>
    public string CreateTokenWithRole(Guid userId, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("email", "test@example.com"),
            new Claim(ClaimTypes.Role, role)
        };
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Stands in for the real service in API tests. It models just enough of the refresh
/// lifecycle — rotation, replay refusal, revocation — for the controller's cookie handling
/// to be exercised. The rules themselves are tested in IdentityServiceRefreshTests.
/// </summary>
public class FakeIdentityService : IIdentityService
{
    private int _counter;
    private readonly HashSet<string> _live = new();
    private readonly HashSet<string> _consumed = new();
    private readonly Dictionary<string, bool> _persistence = new();

    /// <summary>Marks every outstanding token unusable, standing in for expiry.</summary>
    public void ExpireAll() => _live.Clear();

    /// <summary>The lifetimes the controller's cookie expiry is checked against.</summary>
    public static readonly TimeSpan OrdinaryLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan RememberedLifetime = TimeSpan.FromDays(60);

    /// <summary>The choice the last call was made with, for asserting it was passed on.</summary>
    public bool? LastStaySignedIn { get; private set; }

    private TokenDto Issue(bool persistent = false)
    {
        LastStaySignedIn = persistent;
        var refresh = $"refresh-token-{Interlocked.Increment(ref _counter)}";
        _live.Add(refresh);
        _persistence[refresh] = persistent;
        return new TokenDto("fake.jwt.token", DateTime.UtcNow.AddHours(1),
            AuthApiFactory.TestUserId, refresh,
            DateTime.UtcNow.Add(persistent ? RememberedLifetime : OrdinaryLifetime),
            persistent);
    }

    public Task<TokenDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
        => Task.FromResult(Issue(dto.StaySignedIn));

    public Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
        => Task.FromResult(Issue(dto.StaySignedIn));

    public Task<TokenDto> HandleExternalCallbackAsync(string provider, string providerKey,
        string email, string displayName, bool staySignedIn = false,
        CancellationToken ct = default)
        => Task.FromResult(Issue(staySignedIn));

    public Task<UserProfileDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<UserProfileDto?>(new UserProfileDto(
            AuthApiFactory.TestUserId, "test@example.com", "Test User", "Individual"));

    public Task<TokenDto> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        if (_consumed.Contains(rawRefreshToken))
        {
            // Replay: revoke everything outstanding, then refuse.
            _live.Clear();
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        if (!_live.Remove(rawRefreshToken))
            throw new UnauthorizedAccessException("Invalid refresh token.");

        _consumed.Add(rawRefreshToken);
        // A successor inherits the session's chosen length, as the real service does.
        return Task.FromResult(Issue(_persistence.GetValueOrDefault(rawRefreshToken)));
    }

    public Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken)) _live.Clear();
        return Task.CompletedTask;
    }
}

public class FakeAiProvider : IAiProvider
{
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<Chat.Domain.Entities.ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "Fake AI response";
        await Task.Yield();
    }
}

public class StubSubscriptionRepository : ISubscriptionRepository
{
    public Task<Subscription?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<Subscription?>(null);
    public Task<Subscription?> GetByStripeIdAsync(string stripeId, CancellationToken ct = default) => Task.FromResult<Subscription?>(null);
    public Task SaveAsync(Subscription subscription, CancellationToken ct = default) => Task.CompletedTask;
}

public class StubPlanRepository : IPlanRepository
{
    public Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Plan>>(new List<Plan>().AsReadOnly());
    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<Plan?>(null);
}

public class AlwaysEnabledFeatureService : IPlanFeatureService
{
    public Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default) => Task.FromResult(true);
}
