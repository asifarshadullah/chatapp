using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Repositories;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Infrastructure.Tests.Repositories;

/// <summary>
/// The startup step: the list's index, and bringing conversations saved before titles existed up
/// to date. A class of its own, so its fixture's database holds only what these tests put there.
/// </summary>
public class MongoChatRepositoryStartupTests : IClassFixture<MongoDbFixture>
{
    private static readonly DateTime Start = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly IMongoDatabase _database;
    private readonly MongoChatRepository _sut;
    private readonly IMongoCollection<BsonDocument> _raw;

    public MongoChatRepositoryStartupTests(MongoDbFixture fixture)
    {
        _database = fixture.Database;
        _sut = new MongoChatRepository(fixture.Database);
        _raw = fixture.Database.GetCollection<BsonDocument>("conversations");
    }

    /// <summary>A conversation in the shape documents had before titles: no title, activity or reply flag.</summary>
    private async Task<Guid> Legacy(Guid owner, params (string Content, MessageRole Role, DateTime At)[] messages)
    {
        var conversation = await _sut.CreateConversationAsync(owner, "ignored");
        foreach (var (content, role, at) in messages)
            await _sut.AddMessageAsync(conversation.Id, owner, new ChatMessage(Guid.NewGuid(), content, role, at));
        await _raw.UpdateOneAsync(
            new BsonDocument("_id", conversation.Id.ToString()),
            new BsonDocument("$unset", new BsonDocument { { "Title", "" }, { "LastActivityAt", "" }, { "HasReply", "" } }));
        return conversation.Id;
    }

    private Task<BsonDocument> Stored(Guid id) =>
        _raw.Find(new BsonDocument("_id", id.ToString())).FirstAsync();

    [Fact]
    public async Task CreatesThePartialListIndex_WhichTheListQueryUses()
    {
        await MongoChatRepository.EnsureIndexesAsync(_database);

        var indexes = await (await _raw.Indexes.ListAsync()).ToListAsync();
        var listIndex = indexes.Single(i => i["key"].AsBsonDocument == new BsonDocument
        {
            { "OwnerId", 1 }, { "LastActivityAt", -1 }, { "_id", -1 }
        });
        listIndex["partialFilterExpression"].AsBsonDocument.Should().Equal(new BsonDocument("HasReply", true));

        var explained = await _database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", "conversations" },
                    { "filter", new BsonDocument { { "OwnerId", Guid.NewGuid().ToString() }, { "HasReply", true } } },
                    { "sort", new BsonDocument { { "LastActivityAt", -1 }, { "_id", -1 } } },
                    { "limit", 21 },
                }
            },
            { "verbosity", "queryPlanner" },
        });
        var plan = explained["queryPlanner"]["winningPlan"].ToJson();
        plan.Should().Contain("IXSCAN").And.Contain(listIndex["name"].AsString).And.NotContain("\"SORT\"");
    }

    [Fact]
    public async Task ALegacyConversationWithAReply_GetsItsTitleActivityAndReplyFlag_AndIsListed()
    {
        var owner = Guid.NewGuid();
        var id = await Legacy(owner,
            ("  What is\n a monad and why does everybody keep talking about them so much?", MessageRole.User, Start),
            ("A monad is…", MessageRole.Assistant, Start.AddMinutes(1)));

        await MongoChatRepository.EnsureIndexesAsync(_database);

        var doc = await Stored(id);
        doc["Title"].AsString.Should().Be(ConversationTitle.From(
            "  What is\n a monad and why does everybody keep talking about them so much?"));
        doc["LastActivityAt"].ToUniversalTime().Should().Be(Start.AddMinutes(1));
        doc["HasReply"].AsBoolean.Should().BeTrue();
        (await _sut.ListConversationsAsync(owner, 20, null)).Conversations.Select(c => c.Id).Should().Equal(id);
    }

    [Fact]
    public async Task ALegacyConversationWithOnlyAQuestion_IsNotListed()
    {
        var owner = Guid.NewGuid();
        var id = await Legacy(owner, ("Hello?", MessageRole.User, Start));

        await MongoChatRepository.EnsureIndexesAsync(_database);

        var doc = await Stored(id);
        doc["Title"].AsString.Should().Be("Hello?");
        doc["LastActivityAt"].ToUniversalTime().Should().Be(Start);
        doc["HasReply"].AsBoolean.Should().BeFalse();
        (await _sut.ListConversationsAsync(owner, 20, null)).Conversations.Should().BeEmpty();
    }

    [Fact]
    public async Task ALegacyConversationWithNoMessages_GetsADefaultTitleAndItsCreationTime()
    {
        var id = await Legacy(Guid.NewGuid());

        await MongoChatRepository.EnsureIndexesAsync(_database);

        var doc = await Stored(id);
        doc["Title"].AsString.Should().Be("New conversation");
        doc["LastActivityAt"].ToUniversalTime().Should().BeCloseTo(doc["CreatedAt"].ToUniversalTime(), TimeSpan.Zero);
        doc["HasReply"].AsBoolean.Should().BeFalse();
    }

    [Fact]
    public async Task AConversationWithoutAnOwner_IsLeftUntouched()
    {
        var legacy = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "CreatedAt", Start },
            { "Messages", new BsonArray() },
        };
        await _raw.InsertOneAsync(legacy);

        await MongoChatRepository.EnsureIndexesAsync(_database);

        (await _raw.Find(new BsonDocument("_id", legacy["_id"])).FirstAsync()).Should().Equal(legacy);
    }

    [Fact]
    public async Task ASecondRun_ChangesNothing()
    {
        await Legacy(Guid.NewGuid(), ("Q", MessageRole.User, Start), ("A", MessageRole.Assistant, Start.AddSeconds(5)));
        await MongoChatRepository.EnsureIndexesAsync(_database);
        var afterFirst = await _raw.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();

        await MongoChatRepository.EnsureIndexesAsync(_database);

        (await _raw.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).Should().Equal(afterFirst);
    }
}
