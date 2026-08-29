using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Identity.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<IdentityService> _logger;

    public IdentityService(IUserStore store, ITokenGenerator tokenGenerator,
        IRefreshTokenStore refreshTokens, IRefreshTokenSettings refreshSettings,
        ILogger<IdentityService> logger)
    {
        _store = store;
        _tokenGenerator = tokenGenerator;
        _refreshTokens = refreshTokens;
        _refreshSettings = refreshSettings;
        _logger = logger;
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

        return await IssueSessionAsync(user, dto.StaySignedIn, ct);
    }

    /// <summary>Verify email/password and return a token. Throws if credentials are invalid.</summary>
    public async Task<TokenDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var user = await _store.FindByEmailAsync(dto.Email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await IssueSessionAsync(user, dto.StaySignedIn, ct);
    }

    /// <summary>
    /// Called after a successful OAuth callback. Creates a new user if the providerKey is
    /// unknown; returns an existing user's token if the key is already linked.
    /// </summary>
    public async Task<TokenDto> HandleExternalCallbackAsync(string provider, string providerKey,
        string email, string displayName, bool staySignedIn = false,
        CancellationToken ct = default)
    {
        var user = await _store.FindByLoginAsync(provider, providerKey, ct);

        if (user is null)
        {
            user = new AppUser(email, displayName);
            user.AddExternalLogin(new ExternalLogin(provider, providerKey));
            await _store.CreateAsync(user, ct);
        }

        return await IssueSessionAsync(user, staySignedIn, ct);
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

        if (stored.RevokedAt is not null)
            throw Refused();

        if (stored.ConsumedAt is null && !stored.IsUsable(now))
            throw Refused();

        // Looked up before consuming, because the grace path below needs it too.
        var user = await _store.FindByIdAsync(stored.UserId, ct);
        if (user is null)
            throw Refused();

        // Conditional: of two exchanges overlapping in flight, exactly one consumes the
        // credential. Losing says nothing about who the caller is, only that someone else got
        // there first — so the loser is judged by the same rule as any other caller presenting
        // an already-consumed credential.
        if (await _refreshTokens.TryConsumeAsync(stored, now, ct))
        {
            // The successor inherits the session's chosen length, and its window is measured
            // from now — so a session in continued use is never ended by elapsed time alone.
            // It also inherits any ceiling this family carries, which is normally none.
            return await IssueSessionAsync(user, stored.Persistent, ct, stored.FamilyId,
                noLaterThan: stored.SessionExpiresAt);
        }

        return await ExchangeAlreadyConsumedAsync(stored.TokenHash, user, now, ct);
    }

    /// <summary>
    /// Decides what to do for a caller whose credential was already consumed — whether it was
    /// consumed before this exchange began or by an exchange that overtook it.
    ///
    /// Consumed a moment ago, this is the legitimate holder renewing from a second client, and
    /// refusing would sign the user out of every one of them. Consumed longer ago, no
    /// legitimate client would still be holding it, so it was captured and the family goes.
    /// The only input to that judgement is how long ago it was consumed: which caller this is
    /// cannot be known, and guessing would let whoever guesses better keep the session.
    ///
    /// The record is re-read because a concurrent exchange has just written it, and the
    /// decision has to be made against what is stored rather than the copy read beforehand.
    /// </summary>
    private async Task<TokenDto> ExchangeAlreadyConsumedAsync(string tokenHash, AppUser user,
        DateTime now, CancellationToken ct)
    {
        var stored = await _refreshTokens.FindByHashAsync(tokenHash, ct);
        if (stored is null)
            throw Refused();

        if (!stored.IsWithinGrace(now, _refreshSettings.GraceWindow))
        {
            await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
            throw Refused();
        }

        // Grace absorbs what would otherwise have been a replay alarm, so the occurrence has
        // to be recorded: a real attack shows up as repeated grace hits on one family, and
        // nothing else would reveal it.
        _logger.LogWarning(
            "Refresh credential for family {FamilyId} was presented again within the grace " +
            "window; treating it as concurrent renewal rather than a replay.",
            stored.FamilyId);

        // The ceiling is whichever is nearer: one already carried by this family, or the
        // moment the credential being presented would itself have stopped working.
        return await IssueSessionAsync(user, stored.Persistent, ct, stored.FamilyId,
            noLaterThan: Earlier(stored.SessionExpiresAt, stored.PreConsumptionExpiresAt));
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
    /// family and its session length; a fresh authentication starts a new one and takes the
    /// length the user asked for. <paramref name="noLaterThan"/> caps the result, and is set
    /// only on the grace path.
    /// </summary>
    private async Task<TokenDto> IssueSessionAsync(AppUser user, bool persistent,
        CancellationToken ct, Guid? familyId = null, DateTime? noLaterThan = null)
    {
        var access = _tokenGenerator.Generate(user);
        var pair = _tokenGenerator.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.Add(_refreshSettings.LifetimeFor(persistent));

        // A session that has renewed under the grace window carries a ceiling, and every
        // credential issued from it stays under that ceiling. Without inheritance the bound
        // would survive a single exchange: a grace-issued credential is otherwise ordinary, so
        // one routine rotation would slide the session back out to its full length and a
        // replayed credential would escape for the price of one more request. Null for a
        // session that has never renewed under grace, which is the ordinary case.
        if (noLaterThan is { } bound && bound < expiresAt) expiresAt = bound;

        await _refreshTokens.AddAsync(new RefreshToken(
            pair.TokenHash,
            user.Id,
            familyId ?? Guid.NewGuid(),
            expiresAt,
            persistent,
            noLaterThan), ct);

        return access with
        {
            RefreshToken = pair.RawToken,
            RefreshTokenExpiresAt = expiresAt,
            RefreshTokenPersistent = persistent
        };
    }

    /// <summary>The nearer of two moments, ignoring those that are not set.</summary>
    private static DateTime? Earlier(DateTime? left, DateTime? right)
        => left is null ? right
            : right is null ? left
            : left < right ? left : right;

    /// <summary>
    /// One refusal for every reason. Which condition failed is not disclosed, so a caller
    /// cannot probe for which tokens exist.
    /// </summary>
    private static UnauthorizedAccessException Refused()
        => new("Invalid refresh token.");
}
