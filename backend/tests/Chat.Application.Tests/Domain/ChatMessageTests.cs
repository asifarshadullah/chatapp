using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;

namespace Chat.Application.Tests.Domain;

/// <summary>
/// Unit tests for ChatMessage domain entity validation and construction.
/// </summary>
public class ChatMessageTests
{
    [Fact]
    public void Create_WithValidContent_Succeeds()
    {
        // Act
        var message = new ChatMessage("Hello, World!", MessageRole.User);

        // Assert
        message.Id.Should().NotBeEmpty();
        message.Content.Should().Be("Hello, World!");
        message.Role.Should().Be(MessageRole.User);
        message.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_WithAssistantRole_SetsRoleCorrectly()
    {
        // Act
        var message = new ChatMessage("Echo: Hello", MessageRole.Assistant);

        // Assert
        message.Role.Should().Be(MessageRole.Assistant);
    }

    [Fact]
    public void Create_WithNullContent_ThrowsArgumentException()
    {
        // Act
        var act = () => new ChatMessage(null!, MessageRole.User);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("content");
    }

    [Fact]
    public void Create_WithEmptyContent_ThrowsArgumentException()
    {
        // Act
        var act = () => new ChatMessage("", MessageRole.User);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("content");
    }

    [Fact]
    public void Create_WithWhitespaceContent_ThrowsArgumentException()
    {
        // Act
        var act = () => new ChatMessage("   ", MessageRole.User);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("content");
    }

    [Fact]
    public void Create_WithContentOver5000Chars_ThrowsArgumentException()
    {
        // Arrange
        var longContent = new string('a', 5001);

        // Act
        var act = () => new ChatMessage(longContent, MessageRole.User);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("content");
    }

    [Fact]
    public void Create_WithContentExactly5000Chars_Succeeds()
    {
        // Arrange
        var maxContent = new string('a', 5000);

        // Act
        var message = new ChatMessage(maxContent, MessageRole.User);

        // Assert
        message.Content.Should().HaveLength(5000);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        // Act
        var message1 = new ChatMessage("Hello", MessageRole.User);
        var message2 = new ChatMessage("World", MessageRole.User);

        // Assert
        message1.Id.Should().NotBe(message2.Id);
    }
}
