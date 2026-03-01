using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Repositories;
using FluentAssertions;

namespace Chat.Infrastructure.Tests.Repositories;

/// <summary>
/// Unit tests for InMemoryChatRepository.
/// </summary>
public class InMemoryChatRepositoryTests
{
    private readonly InMemoryChatRepository _sut = new();

    [Fact]
    public async Task CreateConversationAsync_ReturnsNewConversation()
    {
        var conversation = await _sut.CreateConversationAsync();

        conversation.Should().NotBeNull();
        conversation.Id.Should().NotBeEmpty();
        conversation.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationAsync_WithValidId_ReturnsConversation()
    {
        var created = await _sut.CreateConversationAsync();

        var retrieved = await _sut.GetConversationAsync(created.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetConversationAsync_WithInvalidId_ReturnsNull()
    {
        var result = await _sut.GetConversationAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddMessageAsync_StoresMessageInConversation()
    {
        var conversation = await _sut.CreateConversationAsync();
        var message = new ChatMessage("Hello", MessageRole.User);

        await _sut.AddMessageAsync(conversation.Id, message);

        var retrieved = await _sut.GetConversationAsync(conversation.Id);
        retrieved!.Messages.Should().HaveCount(1);
        retrieved.Messages[0].Should().Be(message);
    }

    [Fact]
    public async Task AddMessageAsync_WithInvalidConversationId_ThrowsKeyNotFoundException()
    {
        var message = new ChatMessage("Hello", MessageRole.User);

        var act = () => _sut.AddMessageAsync(Guid.NewGuid(), message);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsAllMessagesInOrder()
    {
        var conversation = await _sut.CreateConversationAsync();
        var first = new ChatMessage("First", MessageRole.User);
        var second = new ChatMessage("Second", MessageRole.Assistant);

        await _sut.AddMessageAsync(conversation.Id, first);
        await _sut.AddMessageAsync(conversation.Id, second);

        var messages = await _sut.GetMessagesAsync(conversation.Id);

        messages.Should().HaveCount(2);
        messages[0].Should().Be(first);
        messages[1].Should().Be(second);
    }
}
