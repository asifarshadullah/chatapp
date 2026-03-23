using Chat.Identity.Application.Interfaces;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Chat.Identity.Infrastructure.Data;

/// <summary>
/// MongoDB BSON model for the roles collection.
/// </summary>
internal class RoleDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Name { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();

    public RoleInfo ToRoleInfo() => new(Name, Permissions.AsReadOnly());
}
