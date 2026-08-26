using Chat.Identity.Infrastructure.Configuration;
using Chat.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Chat.Identity.Tests.Services;

/// <summary>
/// Refresh-token generation. The token must be unguessable and the stored hash must not be
/// the token itself, so that a leaked database yields no usable credentials.
/// </summary>
public class JwtTokenGeneratorRefreshTests
{
    private static JwtTokenGenerator Build() => new(Options.Create(new JwtSettings
    {
        Secret = "test-secret-key-that-is-long-enough-for-hmac-sha256",
        Issuer = "chatapp",
        Audience = "chatapp",
        ExpiryMinutes = 60
    }));

    [Fact]
    public void GenerateRefreshToken_ProducesADifferentValueEachTime()
    {
        var generator = Build();

        var tokens = Enumerable.Range(0, 100)
            .Select(_ => generator.GenerateRefreshToken().RawToken)
            .ToList();

        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GenerateRefreshToken_ProducesAHighEntropyValue()
    {
        var pair = Build().GenerateRefreshToken();

        // 256 bits, base64url encoded. Short enough to fit a cookie, long enough that
        // guessing is not a threat worth modelling.
        pair.RawToken.Should().NotBeNullOrWhiteSpace();
        pair.RawToken.Length.Should().BeGreaterThanOrEqualTo(43);
        pair.RawToken.Should().MatchRegex("^[A-Za-z0-9_-]+$", "the token travels in a cookie");
    }

    [Fact]
    public void GenerateRefreshToken_DoesNotStoreTheRawValue()
    {
        var pair = Build().GenerateRefreshToken();

        pair.TokenHash.Should().NotBe(pair.RawToken);
        pair.TokenHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void HashRefreshToken_MatchesTheHashProducedAtGeneration()
    {
        var generator = Build();
        var pair = generator.GenerateRefreshToken();

        // Lookup hashes what the client presents and compares it with what was stored.
        generator.HashRefreshToken(pair.RawToken).Should().Be(pair.TokenHash);
    }

    [Fact]
    public void HashRefreshToken_DiffersForDifferentTokens()
    {
        var generator = Build();

        generator.HashRefreshToken("token-a").Should().NotBe(generator.HashRefreshToken("token-b"));
    }
}
