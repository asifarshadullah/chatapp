using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Identity.Domain.ValueObjects;
using Chat.Identity.Infrastructure.Services;
using FluentAssertions;

namespace Chat.Identity.Tests.Services;

/// <summary>
/// Unit tests for IdentityService business logic.
/// Uses FakeUserStore and FakeTokenGenerator — no real DB or JWT signing key needed.
/// </summary>
public class IdentityServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IdentityService Build(out FakeUserStore store)
    {
        store = new FakeUserStore();
        return new IdentityService(store, new FakeTokenGenerator());
    }

    // ── Phase 1 Cycle 1.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_WithValidData_StoresUserAndReturnsToken()
    {
        var svc = Build(out var store);

        var result = await svc.RegisterAsync(new RegisterDto("test@example.com", "Password123!", "Test User"));

        var user = store.Users.Should().ContainSingle().Subject;
        user.Email.Should().Be("test@example.com");
        BCrypt.Net.BCrypt.Verify("Password123!", user.PasswordHash).Should().BeTrue();
        result.UserId.Should().Be(user.Id);
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── Phase 1 Cycle 1.2 ────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ThrowsInvalidOperationException()
    {
        var svc = Build(out var store);
        store.Users.Add(new AppUser("test@example.com", "Existing User"));

        var act = () => svc.RegisterAsync(new RegisterDto("test@example.com", "Password!", "Another"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    // ── Phase 2 Cycle 2.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        var svc = Build(out var store);
        var user = new AppUser("login@example.com", "Login User");
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("Password123!"));
        store.Users.Add(user);

        var result = await svc.LoginAsync(new LoginDto("login@example.com", "Password123!"));

        result.UserId.Should().Be(user.Id);
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── Phase 2 Cycle 2.2 ────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsUnauthorizedAccessException()
    {
        var svc = Build(out var store);
        var user = new AppUser("login@example.com", "Login User");
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("CorrectPassword!"));
        store.Users.Add(user);

        var act = () => svc.LoginAsync(new LoginDto("login@example.com", "WrongPassword!"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ThrowsUnauthorizedAccessException()
    {
        var svc = Build(out _);

        var act = () => svc.LoginAsync(new LoginDto("nobody@example.com", "Password!"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── Phase 5 Cycle 5.1 ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleExternalCallbackAsync_NewUser_CreatesUserAndReturnsToken()
    {
        var svc = Build(out var store);

        var result = await svc.HandleExternalCallbackAsync(
            "Google", "google-key-123", "google@example.com", "Google User");

        var user = store.Users.Should().ContainSingle().Subject;
        user.Email.Should().Be("google@example.com");
        user.ExternalLogins.Should().ContainSingle(l =>
            l.Provider == "Google" && l.ProviderKey == "google-key-123");
        result.UserId.Should().Be(user.Id);
    }

    // ── Phase 5 Cycle 5.2 ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleExternalCallbackAsync_ExistingUser_ReturnsTokenWithoutCreating()
    {
        var svc = Build(out var store);
        var existing = new AppUser("google@example.com", "Google User");
        existing.AddExternalLogin(new ExternalLogin("Google", "google-key-123"));
        store.Users.Add(existing);

        var result = await svc.HandleExternalCallbackAsync(
            "Google", "google-key-123", "google@example.com", "Google User");

        store.Users.Should().HaveCount(1); // no duplicate
        result.UserId.Should().Be(existing.Id);
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

public class FakeUserStore : IUserStore
{
    public List<AppUser> Users { get; } = new();

    public Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u =>
            u.Email.Equals(email.ToLowerInvariant(), StringComparison.Ordinal)));

    public Task<AppUser?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<AppUser?> FindByLoginAsync(string provider, string providerKey, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u =>
            u.ExternalLogins.Any(l => l.Provider == provider && l.ProviderKey == providerKey)));

    public Task CreateAsync(AppUser user, CancellationToken ct = default)
    {
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppUser user, CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeTokenGenerator : ITokenGenerator
{
    public TokenDto Generate(AppUser user)
        => new("fake.jwt.token", DateTime.UtcNow.AddHours(1), user.Id);
}
