using System.Text;
using Chat.Application.Exceptions;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Repositories;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Infrastructure.Tests.Repositories;

/// <summary>
/// Titles, last activity and the conversation list, against a real MongoDB. Each test uses its own
/// users, since the fixture's database is shared by the whole class.
/// </summary>
public class MongoChatRepositoryListingTests : IClassFixture<MongoDbFixture>
{
    // After every conversation's creation, as a real message always is.
    private static readonly DateTime Start = new(2100, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly MongoChatRepository _sut;
    private readonly IMongoCollection<BsonDocument> _raw;

    public MongoChatRepositoryListingTests(MongoDbFixture fixture)
    {
        _sut = new MongoChatRepository(fixture.Database);
        _raw = fixture.Database.GetCollection<BsonDocument>("conversations");
    }

    private static ChatMessage Message(string content, MessageRole role, DateTime at) =>
        new(Guid.NewGuid(), content, role, at);

    /// <summary>A conversation whose question and reply were saved, the reply at <paramref name="at"/>.</summary>
    private async Task<Guid> Replied(Guid owner, string title, DateTime at)
    {
        var conversation = await _sut.CreateConversationAsync(owner, title);
        await _sut.AddMessageAsync(conversation.Id, owner, Message("Question", MessageRole.User, at.AddSeconds(-1)));
        await _sut.AddMessageAsync(conversation.Id, owner, Message("Answer", MessageRole.Assistant, at));
        return conversation.Id;
    }

    private Task<BsonDocument> Stored(Guid id) =>
        _raw.Find(new BsonDocument("_id", id.ToString())).FirstAsync();

    // ── What is stored ───────────────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresTheTitle_ActivityAtCreation_AndNoReply()
    {
        var conversation = await _sut.CreateConversationAsync(Guid.NewGuid(), "First message");

        var doc = await Stored(conversation.Id);
        doc["Title"].AsString.Should().Be("First message");
        doc["LastActivityAt"].ToUniversalTime().Should().BeCloseTo(conversation.CreatedAt, TimeSpan.FromMilliseconds(1));
        doc["HasReply"].AsBoolean.Should().BeFalse();
        conversation.Title.Should().Be("First message");
    }

    [Fact]
    public async Task AddMessage_MovesTheActivity_AndOnlyAReplyMarksIt()
    {
        var owner = Guid.NewGuid();
        var conversation = await _sut.CreateConversationAsync(owner, "T");

        await _sut.AddMessageAsync(conversation.Id, owner, Message("Q", MessageRole.User, Start));
        var afterQuestion = await Stored(conversation.Id);
        await _sut.AddMessageAsync(conversation.Id, owner, Message("A", MessageRole.Assistant, Start.AddMinutes(1)));
        var afterReply = await Stored(conversation.Id);

        afterQuestion["LastActivityAt"].ToUniversalTime().Should().Be(Start);
        afterQuestion["HasReply"].AsBoolean.Should().BeFalse();
        afterReply["LastActivityAt"].ToUniversalTime().Should().Be(Start.AddMinutes(1));
        afterReply["HasReply"].AsBoolean.Should().BeTrue();
    }

    [Fact]
    public async Task AddMessage_SavedOutOfOrder_DoesNotMoveTheActivityBack()
    {
        var owner = Guid.NewGuid();
        var conversation = await _sut.CreateConversationAsync(owner, "T");

        await _sut.AddMessageAsync(conversation.Id, owner, Message("Later", MessageRole.User, Start.AddMinutes(1)));
        await _sut.AddMessageAsync(conversation.Id, owner, Message("Earlier", MessageRole.Assistant, Start));

        (await Stored(conversation.Id))["LastActivityAt"].ToUniversalTime().Should().Be(Start.AddMinutes(1));
    }

    [Fact]
    public async Task SetTitle_ChangesTheOwnersTitle()
    {
        var owner = Guid.NewGuid();
        var id = await Replied(owner, "Old", Start);

        await _sut.SetTitleAsync(id, owner, "New");

        (await _sut.GetConversationAsync(id, owner))!.Title.Should().Be("New");
    }

    [Fact]
    public async Task SetTitle_ForAnotherUser_ThrowsAndLeavesItUnchanged()
    {
        var owner = Guid.NewGuid();
        var id = await Replied(owner, "Mine", Start);

        var act = () => _sut.SetTitleAsync(id, Guid.NewGuid(), "Theirs");

        await act.Should().ThrowAsync<ConversationNotFoundException>();
        (await _sut.GetConversationAsync(id, owner))!.Title.Should().Be("Mine");
    }

    // ── The list ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsTheOwnersConversations_NewestFirst_WithoutMessages()
    {
        var owner = Guid.NewGuid();
        var older = await Replied(owner, "Older", Start);
        var newer = await Replied(owner, "Newer", Start.AddHours(1));
        await Replied(Guid.NewGuid(), "Someone else's", Start.AddHours(2));

        var page = await _sut.ListConversationsAsync(owner, 20, null);

        page.Conversations.Select(c => (c.Id, c.Title, c.LastActivityAt)).Should().Equal(
            (newer, "Newer", Start.AddHours(1)),
            (older, "Older", Start));
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_ReturnsActivityInUtc()
    {
        var owner = Guid.NewGuid();
        await Replied(owner, "T", Start);

        var page = await _sut.ListConversationsAsync(owner, 20, null);

        page.Conversations.Single().LastActivityAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task List_LeavesOutAConversationUntilItHasAReply()
    {
        var owner = Guid.NewGuid();
        var conversation = await _sut.CreateConversationAsync(owner, "Waiting");
        await _sut.AddMessageAsync(conversation.Id, owner, Message("Q", MessageRole.User, Start));

        var before = await _sut.ListConversationsAsync(owner, 20, null);
        await _sut.AddMessageAsync(conversation.Id, owner, Message("A", MessageRole.Assistant, Start.AddSeconds(1)));
        var after = await _sut.ListConversationsAsync(owner, 20, null);

        before.Conversations.Should().BeEmpty();
        after.Conversations.Select(c => c.Id).Should().Equal(conversation.Id);
    }

    [Fact]
    public async Task List_ForAUserWithNoConversations_IsEmptyWithNoCursor()
    {
        var page = await _sut.ListConversationsAsync(Guid.NewGuid(), 20, null);

        page.Conversations.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_OrdersEqualActivityTheSameWayEveryTime()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await Replied(owner, $"Tied {i}", Start);

        var first = await _sut.ListConversationsAsync(owner, 20, null);
        var second = await _sut.ListConversationsAsync(owner, 20, null);
        var paged = await PageThrough(owner, limit: 2);

        second.Conversations.Select(c => c.Id).Should().Equal(first.Conversations.Select(c => c.Id));
        paged.Should().Equal(first.Conversations.Select(c => c.Id));
    }

    [Fact]
    public async Task List_PagesThroughTwentyFive_EachOnce()
    {
        var owner = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
            ids.Add(await Replied(owner, $"C{i}", Start.AddMinutes(i)));

        var first = await _sut.ListConversationsAsync(owner, 20, null);
        var second = await _sut.ListConversationsAsync(owner, 20, first.NextCursor);

        first.Conversations.Should().HaveCount(20);
        first.NextCursor.Should().NotBeNull();
        second.Conversations.Should().HaveCount(5);
        second.NextCursor.Should().BeNull();
        first.Conversations.Concat(second.Conversations).Select(c => c.Id)
            .Should().Equal(Enumerable.Reverse(ids));
    }

    [Fact]
    public async Task List_WithAnotherUsersCursor_ReturnsOnlyTheCallersConversations()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var alices = await Replied(alice, "Alice's", Start);
        await Replied(bob, "Bob's 1", Start.AddHours(1));
        await Replied(bob, "Bob's 2", Start.AddHours(2));
        var bobsCursor = (await _sut.ListConversationsAsync(bob, 1, null)).NextCursor;

        var page = await _sut.ListConversationsAsync(alice, 20, bobsCursor);

        page.Conversations.Select(c => c.Id).Should().Equal(alices);
    }

    public static TheoryData<string> MalformedCursors => new()
    {
        "not base64 !",
        Base64Url("abc:" + Guid.NewGuid()),
        Base64Url("638000000000000000:not-a-guid"),
        Base64Url("only-one-part"),
    };

    [Theory]
    [MemberData(nameof(MalformedCursors))]
    public async Task List_WithAMalformedCursor_Throws(string cursor)
    {
        var act = () => _sut.ListConversationsAsync(Guid.NewGuid(), 20, cursor);

        await act.Should().ThrowAsync<InvalidCursorException>();
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<List<Guid>> PageThrough(Guid owner, int limit)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        // Bounded, so a cursor that never moves past a tie fails the test instead of hanging it.
        for (var pages = 0; pages < 50; pages++)
        {
            var page = await _sut.ListConversationsAsync(owner, limit, cursor);
            ids.AddRange(page.Conversations.Select(c => c.Id));
            cursor = page.NextCursor;
            if (cursor is null)
                return ids;
        }
        throw new InvalidOperationException("Paging did not reach a last page.");
    }
}
