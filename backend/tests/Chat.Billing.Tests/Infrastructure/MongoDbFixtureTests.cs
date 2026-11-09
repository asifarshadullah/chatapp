using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Billing.Tests.Infrastructure;

public class MongoDbFixtureTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;

    public MongoDbFixtureTests(MongoDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheFixtureDatabase_StoresAndReadsADocument()
    {
        var collection = _fixture.Database.GetCollection<BsonDocument>("probe");
        await collection.InsertOneAsync(new BsonDocument("value", 1));

        var stored = await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        stored["value"].AsInt32.Should().Be(1);
    }
}
