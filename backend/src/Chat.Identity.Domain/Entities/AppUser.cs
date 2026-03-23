using Chat.Identity.Domain.Enums;
using Chat.Identity.Domain.ValueObjects;

namespace Chat.Identity.Domain.Entities;

/// <summary>Identity aggregate root. Lives in Chat.Identity bounded context.</summary>
public class AppUser
{
    public Guid Id { get; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public UserType UserType { get; private set; }
    public IReadOnlyList<ExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();
    public DateTime CreatedAt { get; }

    private readonly List<ExternalLogin> _externalLogins = new();

    /// <summary>Create a brand-new user.</summary>
    public AppUser(string email, string displayName, UserType userType = UserType.Individual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = Guid.NewGuid();
        Email = email.ToLowerInvariant();
        DisplayName = displayName;
        UserType = userType;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Reconstruct from storage (two-constructor pattern).</summary>
    public AppUser(Guid id, string email, string displayName, string passwordHash,
        UserType userType, IEnumerable<ExternalLogin> externalLogins, DateTime createdAt)
    {
        Id = id; Email = email; DisplayName = displayName; PasswordHash = passwordHash;
        UserType = userType; CreatedAt = createdAt;
        _externalLogins.AddRange(externalLogins);
    }

    /// <summary>Set the BCrypt hash after hashing the raw password in the service layer.</summary>
    public void SetPasswordHash(string hash) => PasswordHash = hash;

    /// <summary>Link a third-party provider login to this user.</summary>
    public void AddExternalLogin(ExternalLogin login) => _externalLogins.Add(login);
}
