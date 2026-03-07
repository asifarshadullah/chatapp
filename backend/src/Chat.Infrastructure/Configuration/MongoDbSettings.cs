namespace Chat.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed settings for the MongoDB connection, bound from appsettings.
/// </summary>
public class MongoDbSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}
