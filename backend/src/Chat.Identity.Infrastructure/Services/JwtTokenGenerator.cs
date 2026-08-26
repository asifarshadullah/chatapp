using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Chat.Identity.Application.DTOs;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Domain.Entities;
using Chat.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Chat.Identity.Infrastructure.Services;

/// <summary>
/// Builds and signs a JWT from an AppUser. Claim shape is lean: sub, email, role,
/// account_type. No permissions — those are resolved at runtime from the DB.
/// </summary>
public class JwtTokenGenerator : ITokenGenerator
{
    private readonly JwtSettings _settings;

    public JwtTokenGenerator(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    /// <summary>Generates a signed JWT and wraps it in a TokenDto.</summary>
    public TokenDto Generate(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, "User"),
            new Claim("account_type", user.UserType.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new TokenDto(new JwtSecurityTokenHandler().WriteToken(token), expiry, user.Id);
    }

    /// <summary>
    /// Produces a 256-bit random token and the hash to store for it.
    /// </summary>
    public RefreshTokenPair GenerateRefreshToken()
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return new RefreshTokenPair(raw, HashRefreshToken(raw));
    }

    /// <summary>
    /// SHA-256 rather than a password hash: the token is a high-entropy random value, not a
    /// guessable secret, so a deliberately slow KDF would add latency to every refresh
    /// without making the token any harder to attack.
    /// </summary>
    public string HashRefreshToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
