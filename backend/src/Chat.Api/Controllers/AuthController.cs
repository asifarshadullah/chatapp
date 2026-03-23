using System.Security.Claims;
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
    private readonly IIdentityService _identityService;

    public AuthController(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    /// <summary>Register a new user with email and password. Returns a JWT on success.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TokenDto>> Register(
        [FromBody] RegisterDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _identityService.RegisterAsync(dto, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Log in with email and password. Returns a JWT on success.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenDto>> Login(
        [FromBody] LoginDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _identityService.LoginAsync(dto, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    /// <summary>Initiates Google OIDC login by challenging with the Google scheme.</summary>
    [HttpGet("google")]
    public IActionResult GoogleLogin()
    {
        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), "Auth")
        };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Google redirects here after the user consents.
    /// Reads the authenticated principal, issues our JWT.
    /// </summary>
    [HttpGet("callback/google")]
    public async Task<ActionResult<TokenDto>> GoogleCallback(CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync("ExternalCookie");
        if (!result.Succeeded || result.Principal is null)
            return Unauthorized();

        await HttpContext.SignOutAsync("ExternalCookie");

        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var email = result.Principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email;

        var token = await _identityService.HandleExternalCallbackAsync(
            "Google", providerKey, email, name, ct);
        return Ok(token);
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
}
