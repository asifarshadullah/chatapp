using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;

namespace Chat.Application.Tests.Domain;

/// <summary>
/// Unit tests for the Conversation domain entity.
/// </summary>
public class ConversationTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void Create_RecordsTheOwner()
    {
        var conversation = new Conversation(OwnerId);

        conversation.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public void Create_WithEmptyOwner_ThrowsArgumentException()
    {
        var act = () => new Conversation(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reconstitute_KeepsTheStoredOwner()
    {
        var id = Guid.NewGuid();

        var conversation = new Conversation(id, OwnerId, DateTime.UtcNow);

        conversation.Id.Should().Be(id);
        conversation.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public void Create_And_Reconstitute_KeepTheTitle()
    {
        new Conversation(OwnerId, "A title").Title.Should().Be("A title");
        new Conversation(Guid.NewGuid(), OwnerId, DateTime.UtcNow, "Stored").Title.Should().Be("Stored");
    }

    [Fact]
    public void Reconstitute_WithEmptyOwner_ThrowsArgumentException()
    {
        var act = () => new Conversation(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_GeneratesValidId()
    {
        var conversation = new Conversation(OwnerId);

        conversation.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_StartsWithEmptyMessageList()
    {
        var conversation = new Conversation(OwnerId);

        conversation.Messages.Should().BeEmpty();
    }

    [Fact]
    public void AddMessage_AppendsToMessageList()
    {
        var conversation = new Conversation(OwnerId);
        var message = new ChatMessage("Hello", MessageRole.User);

        conversation.AddMessage(message);

        conversation.Messages.Should().HaveCount(1);
        conversation.Messages[0].Should().Be(message);
    }

    [Fact]
    public void AddMessage_WithNullMessage_ThrowsArgumentNullException()
    {
        var conversation = new Conversation(OwnerId);

        var act = () => conversation.AddMessage(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetMessages_ReturnsMessagesInChronologicalOrder()
    {
        var conversation = new Conversation(OwnerId);
        var first = new ChatMessage("First", MessageRole.User);
        var second = new ChatMessage("Second", MessageRole.Assistant);
        var third = new ChatMessage("Third", MessageRole.User);

        conversation.AddMessage(first);
        conversation.AddMessage(second);
        conversation.AddMessage(third);

        conversation.Messages[0].Should().Be(first);
        conversation.Messages[1].Should().Be(second);
        conversation.Messages[2].Should().Be(third);
    }
}
