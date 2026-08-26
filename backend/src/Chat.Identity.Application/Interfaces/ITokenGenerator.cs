using Chat.Identity.Application.DTOs;
using Chat.Identity.Domain.Entities;

namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// Contract for producing a signed token from an AppUser.
/// Application layer defines the need; Infrastructure provides the JWT implementation.
/// </summary>
public interface ITokenGenerator
{
    TokenDto Generate(AppUser user);

    /// <summary>
    /// Produces a new high-entropy refresh token and the hash to store for it. Generation
    /// only — persisting the hash is the identity service's job.
    /// </summary>
    RefreshTokenPair GenerateRefreshToken();

    /// <summary>Hashes a raw refresh token presented by a client, for lookup.</summary>
    string HashRefreshToken(string rawToken);
}
