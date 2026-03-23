using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Identity.Domain.ValueObjects;

namespace Chat.Identity.Infrastructure.Services;

/// <summary>
/// Orchestrates registration, login, and external-provider callbacks.
/// Depends on IUserStore and ITokenGenerator — no JWT or HTTP knowledge here.
/// </summary>
public class IdentityService : IIdentityService
{
    private readonly IUserStore _store;
    private readonly ITokenGenerator _tokenGenerator;

    public IdentityService(IUserStore store, ITokenGenerator tokenGenerator)
    {
        _store = store;
        _tokenGenerator = tokenGenerator;
    }

    /// <summary>Register a new user with email/password. Throws if email is already taken.</summary>
    public async Task<TokenDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        var existing = await _store.FindByEmailAsync(dto.Email, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Email '{dto.Email}' is already registered.");

        var user = new AppUser(dto.Email, dto.DisplayName);
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(dto.Password));
        await _store.CreateAsync(user, ct);

        return _tokenGenerator.Generate(user);
    }

    /// <summary>Verify email/password and return a token. Throws if credentials are invalid.</summary>
    public async Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var user = await _store.FindByEmailAsync(dto.Email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return _tokenGenerator.Generate(user);
    }

    /// <summary>
    /// Called after a successful OAuth callback. Creates a new user if the providerKey is
    /// unknown; returns an existing user's token if the key is already linked.
    /// </summary>
    public async Task<TokenDto> HandleExternalCallbackAsync(string provider, string providerKey,
        string email, string displayName, CancellationToken ct = default)
    {
        var user = await _store.FindByLoginAsync(provider, providerKey, ct);

        if (user is null)
        {
            user = new AppUser(email, displayName);
            user.AddExternalLogin(new ExternalLogin(provider, providerKey));
            await _store.CreateAsync(user, ct);
        }

        return _tokenGenerator.Generate(user);
    }

    /// <summary>Returns the profile for a given user ID, or null if not found.</summary>
    public async Task<UserProfileDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _store.FindByIdAsync(userId, ct);
        if (user is null) return null;

        return new UserProfileDto(user.Id, user.Email, user.DisplayName, user.UserType.ToString());
    }
}
