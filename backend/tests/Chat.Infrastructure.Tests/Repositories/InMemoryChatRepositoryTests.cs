using Chat.Application.Exceptions;
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
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private readonly InMemoryChatRepository _sut = new();

    [Fact]
    public async Task CreateConversationAsync_ReturnsNewConversation()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");

        conversation.Should().NotBeNull();
        conversation.Id.Should().NotBeEmpty();
        conversation.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationAsync_WithValidId_ReturnsConversation()
    {
        var created = await _sut.CreateConversationAsync(OwnerId, "Saved");

        var retrieved = await _sut.GetConversationAsync(created.Id, OwnerId);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetConversationAsync_WithInvalidId_ReturnsNull()
    {
        var result = await _sut.GetConversationAsync(Guid.NewGuid(), OwnerId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddMessageAsync_StoresMessageInConversation()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        var message = new ChatMessage("Hello", MessageRole.User);

        await _sut.AddMessageAsync(conversation.Id, OwnerId, message);

        var retrieved = await _sut.GetConversationAsync(conversation.Id, OwnerId);
        retrieved!.Messages.Should().HaveCount(1);
        retrieved.Messages[0].Should().Be(message);
    }

    [Fact]
    public async Task AddMessageAsync_WithInvalidConversationId_ThrowsConversationNotFoundException()
    {
        var message = new ChatMessage("Hello", MessageRole.User);

        var act = () => _sut.AddMessageAsync(Guid.NewGuid(), OwnerId, message);

        await act.Should().ThrowAsync<ConversationNotFoundException>();
    }

    [Fact]
    public async Task GetConversationAsync_ForAnotherUser_ReturnsNull()
    {
        var created = await _sut.CreateConversationAsync(OwnerId, "Saved");

        var result = await _sut.GetConversationAsync(created.Id, OtherUserId);

        result.Should().BeNull();
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
    public async Task GetConversationAsync_ReturnsMessagesInOrder()
    {
        var conversation = await _sut.CreateConversationAsync(OwnerId, "Saved");
        var first = new ChatMessage("First", MessageRole.User);
        var second = new ChatMessage("Second", MessageRole.Assistant);

        await _sut.AddMessageAsync(conversation.Id, OwnerId, first);
        await _sut.AddMessageAsync(conversation.Id, OwnerId, second);

        var messages = (await _sut.GetConversationAsync(conversation.Id, OwnerId))!.Messages;

        messages.Should().HaveCount(2);
        messages[0].Should().Be(first);
        messages[1].Should().Be(second);
    }
}
