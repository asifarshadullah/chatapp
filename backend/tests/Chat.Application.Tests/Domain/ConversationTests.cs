using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;

namespace Chat.Application.Tests.Domain;

/// <summary>
/// Unit tests for the Conversation domain entity.
/// </summary>
public class ConversationTests
{
    [Fact]
    public void Create_GeneratesValidId()
    {
        var conversation = new Conversation();

        conversation.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_StartsWithEmptyMessageList()
    {
        var conversation = new Conversation();

        conversation.Messages.Should().BeEmpty();
    }

    [Fact]
    public void AddMessage_AppendsToMessageList()
    {
        var conversation = new Conversation();
        var message = new ChatMessage("Hello", MessageRole.User);

        conversation.AddMessage(message);

        conversation.Messages.Should().HaveCount(1);
        conversation.Messages[0].Should().Be(message);
    }

    [Fact]
    public void AddMessage_WithNullMessage_ThrowsArgumentNullException()
    {
        var conversation = new Conversation();

        var act = () => conversation.AddMessage(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetMessages_ReturnsMessagesInChronologicalOrder()
    {
        var conversation = new Conversation();
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
