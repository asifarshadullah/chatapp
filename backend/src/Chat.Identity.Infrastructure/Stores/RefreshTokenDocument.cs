using Chat.Identity.Domain.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Identity.Infrastructure.Stores;

/// <summary>MongoDB persistence model for RefreshToken. Keeps BSON concerns out of the domain.</summary>
public class RefreshTokenDocument
{
    [BsonId]
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Whether the user asked to stay signed in. Defaults to false, which is also what a
    /// document written before this field existed deserialises to — credentials issued
    /// under the ordinary lifetime keep it, and no backfill is needed.
    /// </summary>
    public bool Persistent { get; set; }

    public static RefreshTokenDocument FromDomain(RefreshToken token) => new()
    {
        Id = token.Id,
        TokenHash = token.TokenHash,
        UserId = token.UserId,
        FamilyId = token.FamilyId,
        ExpiresAt = token.ExpiresAt,
        ConsumedAt = token.ConsumedAt,
        RevokedAt = token.RevokedAt,
        Persistent = token.Persistent
    };

    public RefreshToken ToDomain() => new(
        Id, TokenHash, UserId, FamilyId, ExpiresAt, ConsumedAt, RevokedAt, Persistent);
}
