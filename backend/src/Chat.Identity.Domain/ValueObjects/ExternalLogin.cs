namespace Chat.Identity.Domain.ValueObjects;

/// <summary>Immutable record of a third-party provider login linked to an AppUser.</summary>
public record ExternalLogin(string Provider, string ProviderKey);
