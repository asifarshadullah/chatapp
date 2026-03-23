using Chat.Identity.Domain.Entities;
using Chat.Identity.Domain.Enums;
using Chat.Identity.Domain.ValueObjects;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Identity.Infrastructure.Stores;

/// <summary>MongoDB persistence model for AppUser. Keeps BSON concerns out of the domain.</summary>
public class UserDocument
{
    [BsonId]
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string UserType { get; set; } = "Individual";
    public List<ExternalLoginDocument> ExternalLogins { get; set; } = new();
    public DateTime CreatedAt { get; set; }

    public static UserDocument FromDomain(AppUser user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        PasswordHash = user.PasswordHash,
        UserType = user.UserType.ToString(),
        ExternalLogins = user.ExternalLogins
            .Select(l => new ExternalLoginDocument { Provider = l.Provider, ProviderKey = l.ProviderKey })
            .ToList(),
        CreatedAt = user.CreatedAt
    };

    public AppUser ToDomain() => new(
        Id, Email, DisplayName, PasswordHash,
        Enum.Parse<UserType>(UserType),
        ExternalLogins.Select(l => new ExternalLogin(l.Provider, l.ProviderKey)),
        CreatedAt);
}

public class ExternalLoginDocument
{
    public string Provider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
}
