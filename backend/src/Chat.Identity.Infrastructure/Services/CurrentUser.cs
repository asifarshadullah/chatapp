using System.Security.Claims;
using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Chat.Identity.Infrastructure.Services;

/// <summary>
/// Reads the authenticated caller's claims from IHttpContextAccessor.
/// Application layer sees only ICurrentUser — no HttpContext leaks through.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    /// <summary>The authenticated user's ID parsed from the 'sub' claim.</summary>
    public Guid UserId
    {
        get
        {
            var value = User?.FindFirstValue("sub");
            return value is not null && Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public string Email => User?.FindFirstValue("email") ?? string.Empty;

    public string Role => User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
