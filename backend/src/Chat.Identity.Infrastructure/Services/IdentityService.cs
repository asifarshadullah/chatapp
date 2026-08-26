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
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IRefreshTokenSettings _refreshSettings;

    public IdentityService(IUserStore store, ITokenGenerator tokenGenerator,
        IRefreshTokenStore refreshTokens, IRefreshTokenSettings refreshSettings)
    {
        _store = store;
        _tokenGenerator = tokenGenerator;
        _refreshTokens = refreshTokens;
        _refreshSettings = refreshSettings;
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

        return await IssueSessionAsync(user, ct);
    }

    /// <summary>Verify email/password and return a token. Throws if credentials are invalid.</summary>
    public async Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var user = await _store.FindByEmailAsync(dto.Email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await IssueSessionAsync(user, ct);
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

        return await IssueSessionAsync(user, ct);
    }

    /// <summary>Returns the profile for a given user ID, or null if not found.</summary>
    public async Task<UserProfileDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _store.FindByIdAsync(userId, ct);
        if (user is null) return null;

        return new UserProfileDto(user.Id, user.Email, user.DisplayName, user.UserType.ToString());
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and rotates the credential.
    ///
    /// A consumed token presented again means it was captured and replayed: a legitimate
    /// client discards each token as it uses it. There is no way to tell whether the caller
    /// or the original holder is the attacker, so the whole family is revoked and both must
    /// authenticate again. Losing a session beats an undetected hijack.
    /// </summary>
    public async Task<TokenDto> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            throw Refused();

        var hash = _tokenGenerator.HashRefreshToken(rawRefreshToken);
        var stored = await _refreshTokens.FindByHashAsync(hash, ct);
        if (stored is null)
            throw Refused();

        var now = DateTime.UtcNow;

        if (stored.ConsumedAt is not null)
        {
            await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
            throw Refused();
        }

        if (!stored.IsUsable(now))
            throw Refused();

        var user = await _store.FindByIdAsync(stored.UserId, ct);
        if (user is null)
            throw Refused();

        stored.Consume(now);
        await _refreshTokens.UpdateAsync(stored, ct);

        return await IssueSessionAsync(user, ct, stored.FamilyId);
    }

    /// <summary>
    /// Revokes the family the token belongs to, so signing out ends the ability to obtain new
    /// access tokens rather than only clearing client state. Silent when the token is absent
    /// or unknown — sign-out must not report whether a credential was real.
    /// </summary>
    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;

        var hash = _tokenGenerator.HashRefreshToken(rawRefreshToken);
        var stored = await _refreshTokens.FindByHashAsync(hash, ct);
        if (stored is null) return;

        await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, DateTime.UtcNow, ct);
    }

    /// <summary>
    /// Issues an access token plus a refresh token. A successor inherits its predecessor's
    /// family; a fresh authentication starts a new one.
    /// </summary>
    private async Task<TokenDto> IssueSessionAsync(AppUser user, CancellationToken ct,
        Guid? familyId = null)
    {
        var access = _tokenGenerator.Generate(user);
        var pair = _tokenGenerator.GenerateRefreshToken();

        await _refreshTokens.AddAsync(new RefreshToken(
            pair.TokenHash,
            user.Id,
            familyId ?? Guid.NewGuid(),
            DateTime.UtcNow.Add(_refreshSettings.Lifetime)), ct);

        return access with { RefreshToken = pair.RawToken };
    }

    /// <summary>
    /// One refusal for every reason. Which condition failed is not disclosed, so a caller
    /// cannot probe for which tokens exist.
    /// </summary>
    private static UnauthorizedAccessException Refused()
        => new("Invalid refresh token.");
}
