using System.Runtime.CompilerServices;
using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;

namespace Chat.Application.Tests.Services;

/// <summary>
/// Unit tests for ChatService business logic.
/// Uses hand-crafted fakes for both repository and AI provider.
/// </summary>
public class ChatServiceTests
{
    private readonly FakeChatRepository _repository;
    private readonly FakeAiProvider _aiProvider;
    private readonly ChatService _sut;

    public ChatServiceTests()
    {
        _repository = new FakeChatRepository();
        _aiProvider = new FakeAiProvider();
        _sut = new ChatService(_repository, _aiProvider);
    }

    // ── SendMessageAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WithValidContent_ReturnsAiResponse()
    {
        var result = await _sut.SendMessageAsync("Hello");

        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Message.Should().Be("AI response");
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

    // ── StreamResponseAsync ─────────────────────────────────────────────────

    // Cycle 2.2
    [Fact]
    public async Task StreamResponseAsync_YieldsTokensFromAiProvider()
    {
        _aiProvider.Tokens = ["Hello", " World"];

        var tokens = new List<string>();
        await foreach (var (_, token) in _sut.StreamResponseAsync("Hi"))
            tokens.Add(token);

        tokens.Should().Equal("Hello", " World");
    }

    // Cycle 2.3
    [Fact]
    public async Task StreamResponseAsync_ConversationIdPresentOnEveryItem()
    {
        var items = new List<(Guid ConversationId, string Token)>();
        await foreach (var item in _sut.StreamResponseAsync("Hi"))
            items.Add(item);

        items.Should().NotBeEmpty();
        items.Should().AllSatisfy(i => i.ConversationId.Should().NotBeEmpty());
    }

    // Cycle 2.4
    [Fact]
    public async Task StreamResponseAsync_SavesUserMessageBeforeAnyTokenYielded()
    {
        // Break after the first token — assistant message is only saved after stream completes,
        // so at this point only the user message should exist.
        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("Hello"))
        {
            conversationId = convId;
            break;
        }

        var history = await _sut.GetHistoryAsync(conversationId);
        history!.Messages.Should().ContainSingle(m => m.Role == "user" && m.Content == "Hello");
    }

    // Cycle 2.5
    [Fact]
    public async Task StreamResponseAsync_SavesCompleteAssistantMessageAfterStreamCompletes()
    {
        _aiProvider.Tokens = ["Hello", " World"];

        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("Hi"))
            conversationId = convId;

        var history = await _sut.GetHistoryAsync(conversationId);
        history!.Messages.Should().HaveCount(2);
        history.Messages[1].Role.Should().Be("assistant");
        history.Messages[1].Content.Should().Be("Hello World");
    }

    // Cycle 2.6
    [Fact]
    public async Task StreamResponseAsync_PassesFullHistoryIncludingUserMessageToProvider()
    {
        await foreach (var _ in _sut.StreamResponseAsync("Hello")) { }

        _aiProvider.ReceivedHistory.Should().NotBeNull();
        _aiProvider.ReceivedHistory!.Should().ContainSingle(m =>
            m.Role == MessageRole.User && m.Content == "Hello");
    }

    // Cycle 2.7
    [Fact]
    public async Task StreamResponseAsync_WithNoConversationId_CreatesNewConversation()
    {
        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("Hello"))
            conversationId = convId;

        conversationId.Should().NotBeEmpty();
    }

    // ── GetHistoryAsync ─────────────────────────────────────────────────────

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

    // ── test doubles ────────────────────────────────────────────────────────

    private sealed class FakeAiProvider : IAiProvider
    {
        public IEnumerable<string> Tokens { get; set; } = ["AI response"];
        public IReadOnlyList<ChatMessage>? ReceivedHistory { get; private set; }

        public async IAsyncEnumerable<string> StreamCompletionAsync(
            IReadOnlyList<ChatMessage> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ReceivedHistory = history;
            foreach (var token in Tokens)
            {
                yield return token;
                await Task.Yield();
            }
        }
    }

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
