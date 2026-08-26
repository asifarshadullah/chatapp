using System.Security.Claims;
using Chat.Api.Contracts;
using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

/// <summary>
/// Handles email/password registration + login, Google OAuth, and current-user profile.
/// Thin controller — all logic delegated to IIdentityService.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Name of the http-only cookie carrying the refresh token. Scoped to /auth so it is not
    /// attached to ordinary API calls that have no use for it.
    /// </summary>
    private const string RefreshCookieName = "refresh_token";
    private const string RefreshCookiePath = "/auth";

    /// <summary>
    /// Where the user's "keep me signed in" choice rides across the provider round trip.
    /// The framework carries authentication properties in its own encrypted, signed state,
    /// so what comes back is what the challenge wrote and not what a caller supplied.
    /// </summary>
    private const string StaySignedInKey = "stay_signed_in";

    private readonly IIdentityService _identityService;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IIdentityService identityService, IWebHostEnvironment environment)
    {
        _identityService = identityService;
        _environment = environment;
    }

    /// <summary>Register a new user with email and password. Returns a JWT on success.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _identityService.RegisterAsync(dto, ct);
            return Ok(IssueSession(result));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Log in with email and password. Returns a JWT on success.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _identityService.LoginAsync(dto, ct);
            return Ok(IssueSession(result));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    /// <summary>
    /// Initiates Google OIDC login by challenging with the Google scheme, carrying the
    /// user's "keep me signed in" choice through to the callback.
    /// </summary>
    [HttpGet("google")]
    public IActionResult GoogleLogin([FromQuery] bool staySignedIn = false)
    {
        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), "Auth")
        };
        props.Items[StaySignedInKey] = staySignedIn.ToString();
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Google redirects here after the user consents.
    /// Reads the authenticated principal, issues our JWT.
    /// </summary>
    [HttpGet("callback/google")]
    public async Task<ActionResult<AuthResponse>> GoogleCallback(CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync("ExternalCookie");
        if (!result.Succeeded || result.Principal is null)
            return Unauthorized();

        await HttpContext.SignOutAsync("ExternalCookie");

        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var email = result.Principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email;

        // Anything other than what the challenge wrote — absent, empty, or unparseable —
        // is not the user's choice, and falls back to the shorter session.
        var staySignedIn = result.Properties?.Items.TryGetValue(StaySignedInKey, out var carried) == true
            && bool.TryParse(carried, out var parsed) && parsed;

        var token = await _identityService.HandleExternalCallbackAsync(
            "Google", providerKey, email, name, staySignedIn, ct);
        return Ok(IssueSession(token));
    }

    /// <summary>Returns the authenticated caller's profile. Requires a valid JWT.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> Me(
        [FromServices] ICurrentUser currentUser,
        CancellationToken ct)
    {
        var profile = await _identityService.GetUserAsync(currentUser.UserId, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token, rotating the cookie.
    ///
    /// Deliberately not [Authorize]: the whole point is to renew after the access token has
    /// already lapsed, so requiring one would make the endpoint useless.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(raw))
            return Unauthorized();

        try
        {
            var result = await _identityService.RefreshAsync(raw, ct);
            return Ok(IssueSession(result));
        }
        catch (UnauthorizedAccessException)
        {
            // The credential is spent, unknown, expired or replayed — the client cannot act
            // on the difference, and disclosing it would let a caller probe for live tokens.
            ClearRefreshCookie();
            return Unauthorized();
        }
    }

    /// <summary>
    /// Signs out by revoking the token's family and clearing the cookie, so signing out ends
    /// the ability to obtain new access tokens rather than only discarding client state.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _identityService.LogoutAsync(Request.Cookies[RefreshCookieName], ct);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>
    /// Moves the refresh token out of the DTO and into an http-only cookie, so it never
    /// reaches the response body.
    /// </summary>
    private AuthResponse IssueSession(TokenDto token)
    {
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            // Expires only for a session the user asked to be remembered; without it the
            // browser drops the cookie when it closes, which is what declining means on a
            // shared machine. The expiry comes from the service so the cookie can never
            // outlive the credential it carries.
            DateTimeOffset? expires = token.RefreshTokenPersistent
                ? new DateTimeOffset(token.RefreshTokenExpiresAt, TimeSpan.Zero)
                : null;

            Response.Cookies.Append(RefreshCookieName, token.RefreshToken, CookieOptions(expires));
        }

        return AuthResponse.From(token);
    }

    private void ClearRefreshCookie()
        => Response.Cookies.Append(RefreshCookieName, string.Empty,
            CookieOptions(DateTimeOffset.UnixEpoch));

    /// <summary>
    /// HttpOnly always, so script cannot read the credential. Secure everywhere except
    /// development, where the API is plain HTTP on localhost and a Secure cookie would simply
    /// never be sent. SameSite=Lax rather than Strict so the OAuth redirect back into the app
    /// still carries it; the refresh endpoint is a POST, which Lax withholds cross-site.
    /// </summary>
    private Microsoft.AspNetCore.Http.CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !_environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = RefreshCookiePath,
        Expires = expires
    };

    /// <summary>Probe endpoint for AdminOnly policy integration tests. Returns 200 for Admin, 403 otherwise.</summary>
    [HttpGet("admin-probe")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult AdminProbe() => Ok();
}
