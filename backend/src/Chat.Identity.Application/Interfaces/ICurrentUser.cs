namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Exposes the authenticated caller's identity to any Application-layer consumer.
/// No HttpContext or JWT details leak through this interface.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}
