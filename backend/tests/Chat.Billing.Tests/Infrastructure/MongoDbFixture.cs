using MongoDB.Driver;

namespace Chat.Billing.Tests.Infrastructure;

/// <summary>
/// Throwaway databases on the Docker Compose MongoDB, dropped when the test class is done.
/// </summary>
public class MongoDbFixture : IDisposable
{
    private const string ConnectionString = "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";
    private readonly MongoClient _client = new(ConnectionString);
    private readonly List<string> _databases = [];

    public MongoDbFixture() => Database = NewDatabase();

    /// <summary>The database the test class shares.</summary>
    public IMongoDatabase Database { get; }

    /// <summary>A database of its own, for a test that must see a collection no other test wrote to.</summary>
    public IMongoDatabase NewDatabase()
    {
        var name = $"chatapp_billing_test_{Guid.NewGuid():N}";
        lock (_databases)
            _databases.Add(name);
        return _client.GetDatabase(name);
    }

    public void Dispose()
    {
        foreach (var name in _databases)
            _client.DropDatabase(name);
    }
}
