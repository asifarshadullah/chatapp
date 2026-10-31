using Chat.Application.Exceptions;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Repositories;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Infrastructure.Tests.Repositories;

/// <summary>
/// Fixture that creates a unique MongoDB database per test run and drops it on dispose.
/// Requires Docker Compose MongoDB to be running (docker compose up -d).
/// </summary>
public class MongoDbFixture : IDisposable
{
    private const string ConnectionString = "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";
    public IMongoDatabase Database { get; }
    private readonly string _databaseName;
    private readonly MongoClient _client;

    public MongoDbFixture()
    {
        _databaseName = $"chatapp_test_{Guid.NewGuid():N}";
        _client = new MongoClient(ConnectionString);
        Database = _client.GetDatabase(_databaseName);
    }

    public void Dispose() => _client.DropDatabase(_databaseName);
}

/// <summary>
/// Integration tests for MongoChatRepository against a real MongoDB instance.
/// </summary>
public class MongoChatRepositoryTests : IClassFixture<MongoDbFixture>
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private readonly MongoChatRepository _sut;
    private readonly MongoDbFixture _fixture;

    public MongoChatRepositoryTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
        _sut = new MongoChatRepository(fixture.Database);
    }

    // ── Cycle 2.2 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateConversationAsync_StoresConversationInDatabase()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");

        conversation.Should().NotBeNull();
        conversation.Id.Should().NotBeEmpty();

        // Verify it actually went to MongoDB by retrieving it fresh
        var retrieved = await _sut.GetConversationAsync(conversation.Id, OwnerId);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(conversation.Id);
    }

    // ── Cycle 2.3 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConversationAsync_WithValidId_ReturnsStoredConversation()
    {
        var created = await _sut.CreateConversationAsync(OwnerId, "Saved");

        var retrieved = await _sut.GetConversationAsync(created.Id, OwnerId);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    // ── Cycle 2.4 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConversationAsync_WithInvalidId_ReturnsNull()
    {
        var result = await _sut.GetConversationAsync(Guid.NewGuid(), OwnerId);

        result.Should().BeNull();
    }

    // ── Cycle 2.5 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMessageAsync_PersistsMessageToDatabase()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        var message = new ChatMessage("Hello from MongoDB", MessageRole.User);

        await _sut.AddMessageAsync(conversation.Id, OwnerId, message);

        // Retrieve via a new repo instance to confirm it hit the DB
        var freshRepo = new MongoChatRepository(_fixture.Database);
        var retrieved = await freshRepo.GetConversationAsync(conversation.Id, OwnerId);
        retrieved!.Messages.Should().HaveCount(1);
        retrieved.Messages[0].Content.Should().Be("Hello from MongoDB");
        retrieved.Messages[0].Role.Should().Be(MessageRole.User);
    }

    // ── Cycle 2.6 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConversationAsync_ReturnsAllStoredMessages()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("First", MessageRole.User));
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("Second", MessageRole.Assistant));

        var messages = (await _sut.GetConversationAsync(conversation.Id, OwnerId))!.Messages;

        messages.Should().HaveCount(2);
        messages[0].Content.Should().Be("First");
        messages[1].Content.Should().Be("Second");
    }

    // ── Cycle 2.7 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConversationAsync_ReturnsMessagesInChronologicalOrder()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("Alpha", MessageRole.User));
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("Beta", MessageRole.Assistant));
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("Gamma", MessageRole.User));

        var messages = (await _sut.GetConversationAsync(conversation.Id, OwnerId))!.Messages;

        messages.Select(m => m.Content).Should().ContainInOrder("Alpha", "Beta", "Gamma");
    }

    // ── Bonus verification ──────────────────────────────────────────────────

    [Fact]
    public async Task Conversation_PersistsAcrossRepositoryInstances()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("Persisted", MessageRole.User));

        // Simulate a server restart by creating a brand-new repository instance
        var freshRepo = new MongoChatRepository(_fixture.Database);
        var messages = (await freshRepo.GetConversationAsync(conversation.Id, OwnerId))!.Messages;

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Be("Persisted");
    }

    // ── Ownership ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConversationAsync_ForAnotherUser_ReturnsNull()
    {
        var created = await _sut.CreateConversationAsync(OwnerId, "Saved");

        var result = await _sut.GetConversationAsync(created.Id, OtherUserId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Conversation_KeepsItsOwnerAcrossRepositoryInstances()
    {
        var created = await _sut.CreateConversationAsync(OwnerId, "Saved");

        var freshRepo = new MongoChatRepository(_fixture.Database);
        var retrieved = await freshRepo.GetConversationAsync(created.Id, OwnerId);

        retrieved!.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task AddMessageAsync_FromAnotherUser_ThrowsAndLeavesConversationUnchanged()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        await _sut.AddMessageAsync(conversation.Id, OwnerId, new ChatMessage("Mine", MessageRole.User));

        var act = () => _sut.AddMessageAsync(
            conversation.Id, OtherUserId, new ChatMessage("Intruder", MessageRole.User));

        await act.Should().ThrowAsync<ConversationNotFoundException>();
        var retrieved = await _sut.GetConversationAsync(conversation.Id, OwnerId);
        retrieved!.Messages.Select(m => m.Content).Should().Equal("Mine");
    }

    [Fact]
    public async Task ConversationStoredWithoutAnOwner_IsUnreachableToEveryUser()
    {
        // The shape documents had before conversations carried an owner.
        var legacyId = Guid.NewGuid();
        await _fixture.Database.GetCollection<BsonDocument>("conversations").InsertOneAsync(new BsonDocument
        {
            { "_id", legacyId.ToString() },
            { "CreatedAt", DateTime.UtcNow },
            { "Messages", new BsonArray() }
        });

        var read = await _sut.GetConversationAsync(legacyId, OwnerId);
        var write = () => _sut.AddMessageAsync(legacyId, OwnerId, new ChatMessage("Hello", MessageRole.User));

        read.Should().BeNull();
        await write.Should().ThrowAsync<ConversationNotFoundException>();
    }
}
