using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;

namespace Chat.Application.Tests.Services;

/// <summary>
/// Unit tests for ChatService business logic.
/// Uses a hand-crafted fake repository to keep tests isolated from infrastructure.
/// </summary>
public class ChatServiceTests
{
    private readonly FakeChatRepository _repository;
    private readonly ChatService _sut;

    public ChatServiceTests()
    {
        _repository = new FakeChatRepository();
        _sut = new ChatService(_repository);
    }

    // ── existing echo behaviour ─────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WithValidContent_ReturnsEchoResponse()
    {
        var result = await _sut.SendMessageAsync("Hello");

        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Message.Should().Be("Echo: Hello");
        result.Role.Should().Be("assistant");
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendMessageAsync_WithNullContent_ThrowsArgumentException()
    {
        var act = () => _sut.SendMessageAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_WithEmptyContent_ThrowsArgumentException()
    {
        var act = () => _sut.SendMessageAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_WithWhitespaceContent_ThrowsArgumentException()
    {
        var act = () => _sut.SendMessageAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── conversation history ────────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WithNoConversationId_CreatesNewConversation()
    {
        var result = await _sut.SendMessageAsync("Hello");

        result.ConversationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_WithExistingConversationId_AppendsToConversation()
    {
        var first = await _sut.SendMessageAsync("Hello");
        var second = await _sut.SendMessageAsync("World", first.ConversationId);

        second.ConversationId.Should().Be(first.ConversationId);
    }

    [Fact]
    public async Task SendMessageAsync_StoresBothUserAndAssistantMessages()
    {
        var result = await _sut.SendMessageAsync("Hello");

        var history = await _sut.GetHistoryAsync(result.ConversationId);
        history.Should().NotBeNull();
        history!.Messages.Should().HaveCount(2);
        history.Messages[0].Role.Should().Be("user");
        history.Messages[1].Role.Should().Be("assistant");
    }

    [Fact]
    public async Task SendMessageAsync_ReturnsConversationIdInResponse()
    {
        var result = await _sut.SendMessageAsync("Hi");

        result.ConversationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_WithValidId_ReturnsHistory()
    {
        var sent = await _sut.SendMessageAsync("Hello");

        var history = await _sut.GetHistoryAsync(sent.ConversationId);

        history.Should().NotBeNull();
        history!.ConversationId.Should().Be(sent.ConversationId);
    }

    [Fact]
    public async Task GetHistoryAsync_WithInvalidId_ReturnsNull()
    {
        var history = await _sut.GetHistoryAsync(Guid.NewGuid());

        history.Should().BeNull();
    }

    // ── test double ─────────────────────────────────────────────────────────

    private sealed class FakeChatRepository : IChatRepository
    {
        private readonly Dictionary<Guid, Conversation> _store = new();

        public Task<Conversation> CreateConversationAsync(CancellationToken ct = default)
        {
            var conversation = new Conversation();
            _store[conversation.Id] = conversation;
            return Task.FromResult(conversation);
        }

        public Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default)
        {
            _store.TryGetValue(conversationId, out var conversation);
            return Task.FromResult(conversation);
        }

        public Task AddMessageAsync(Guid conversationId, ChatMessage message, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(conversationId, out var conversation))
                throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");
            conversation.AddMessage(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(conversationId, out var conversation))
                throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");
            return Task.FromResult(conversation.Messages);
        }
    }
}
