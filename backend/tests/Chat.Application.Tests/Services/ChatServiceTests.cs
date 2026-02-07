using Chat.Application.DTOs;
using Chat.Application.Services;
using FluentAssertions;

namespace Chat.Application.Tests.Services;

/// <summary>
/// Unit tests for ChatService business logic.
/// </summary>
public class ChatServiceTests
{
    private readonly ChatService _sut;

    public ChatServiceTests()
    {
        _sut = new ChatService();
    }

    [Fact]
    public async Task SendMessageAsync_WithValidContent_ReturnsEchoResponse()
    {
        // Arrange
        var content = "Hello";

        // Act
        var result = await _sut.SendMessageAsync(content);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Message.Should().Be("Echo: Hello");
        result.Role.Should().Be("assistant");
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendMessageAsync_WithNullContent_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.SendMessageAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyContent_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.SendMessageAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_WithWhitespaceContent_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.SendMessageAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
