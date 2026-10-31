using System.Runtime.CompilerServices;
using Chat.Application.DTOs;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.Application.Tests.Services;

/// <summary>
/// Unit tests for ChatService business logic.
/// Uses hand-crafted fakes for both repository and AI provider.
/// </summary>
public class ChatServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private readonly FakeChatRepository _repository;
    private readonly FakeAiProvider _aiProvider;
    private readonly FakeQuota _quota;
    private readonly FakeTitleGenerator _titles;
    private readonly ChatService _sut;

    public ChatServiceTests()
    {
        _repository = new FakeChatRepository();
        _aiProvider = new FakeAiProvider();
        _quota = new FakeQuota();
        _titles = new FakeTitleGenerator();
        _sut = new ChatService(_repository, _aiProvider, _quota, _titles, NullLogger<ChatService>.Instance);
    }

    // ── StreamResponseAsync ─────────────────────────────────────────────────

    // Cycle 2.2
    [Fact]
    public async Task StreamResponseAsync_YieldsTokensFromAiProvider()
    {
        _aiProvider.Tokens = ["Hello", " World"];

        var tokens = new List<string>();
        await foreach (var (_, token) in _sut.StreamResponseAsync("Hi", UserId))
            tokens.Add(token);

        tokens.Should().Equal("Hello", " World");
    }

    // Cycle 2.3
    [Fact]
    public async Task StreamResponseAsync_ConversationIdPresentOnEveryItem()
    {
        var items = new List<(Guid ConversationId, string Token)>();
        await foreach (var item in _sut.StreamResponseAsync("Hi", UserId))
            items.Add(item);

        items.Should().NotBeEmpty();
        items.Should().AllSatisfy(i => i.ConversationId.Should().NotBeEmpty());
    }

    // Cycle 2.4
    [Fact]
    public async Task StreamResponseAsync_SavesUserMessageBeforeAnyTokenYielded()
    {
        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("Hello", UserId))
        {
            conversationId = convId;
            break;
        }

        var history = await _sut.GetHistoryAsync(conversationId, UserId);
        history!.Messages.Should().ContainSingle(m => m.Role == "user" && m.Content == "Hello");
    }

    // Cycle 2.5
    [Fact]
    public async Task StreamResponseAsync_SavesCompleteAssistantMessageAfterStreamCompletes()
    {
        _aiProvider.Tokens = ["Hello", " World"];

        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("Hi", UserId))
            conversationId = convId;

        var history = await _sut.GetHistoryAsync(conversationId, UserId);
        history!.Messages.Should().HaveCount(2);
        history.Messages[1].Role.Should().Be("assistant");
        history.Messages[1].Content.Should().Be("Hello World");
    }

    // Cycle 2.6
    [Fact]
    public async Task StreamResponseAsync_PassesFullHistoryIncludingUserMessageToProvider()
    {
        await foreach (var _ in _sut.StreamResponseAsync("Hello", UserId)) { }

        _aiProvider.ReceivedHistory.Should().NotBeNull();
        _aiProvider.ReceivedHistory!.Should().ContainSingle(m =>
            m.Role == MessageRole.User && m.Content == "Hello");
    }

    // Cycle 2.7
    [Fact]
    public async Task StreamResponseAsync_WithNoConversationId_CreatesNewConversation()
    {
        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("Hello", UserId))
            conversationId = convId;

        conversationId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StreamResponseAsync_WithBlankContent_ThrowsBeforeCreatingOrCallingAi(string? content)
    {
        // The iterator is lazy: nothing runs until the first MoveNextAsync, so drain it.
        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync(content!, UserId)) { }
        };

        await act.Should().ThrowAsync<ArgumentException>();
        _repository.ConversationCount.Should().Be(0);
        _aiProvider.ReceivedHistory.Should().BeNull();
    }

    [Fact]
    public async Task StreamResponseAsync_WithExistingConversationId_AppendsToThatConversation()
    {
        var first = await StartConversationAsync("Hello", UserId);

        Guid secondId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync("World", UserId, first))
            secondId = convId;

        secondId.Should().Be(first);
        _repository.ConversationCount.Should().Be(1);
        var history = await _sut.GetHistoryAsync(first, UserId);
        history!.Messages.Select(m => (m.Role, m.Content)).Should().Equal(
            ("user", "Hello"), ("assistant", "AI response"), ("user", "World"), ("assistant", "AI response"));
    }

    // ── Replies that stop part-way ──────────────────────────────────────────

    [Fact]
    public async Task StreamResponseAsync_ProviderFailsAfterTokens_SavesThePartialReplyAndRethrows()
    {
        _aiProvider.Tokens = ["Hel", "lo"];
        _aiProvider.ThrowAfterTokens = new InvalidOperationException("model failed");

        Guid conversationId = Guid.Empty;
        var act = async () =>
        {
            await foreach (var (convId, _) in _sut.StreamResponseAsync("Hi", UserId))
                conversationId = convId;
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("model failed");
        await ShouldHoldMessagesAsync(conversationId, ("user", "Hi"), ("assistant", "Hello"));
    }

    [Fact]
    public async Task StreamResponseAsync_CancelledAfterTokens_SavesThePartialReply()
    {
        _aiProvider.Tokens = ["Hel", "lo"];
        _aiProvider.WaitForCancellationAfterTokens = true;
        using var cts = new CancellationTokenSource();

        Guid conversationId = Guid.Empty;
        var received = 0;
        var act = async () =>
        {
            await foreach (var (convId, _) in _sut.StreamResponseAsync("Hi", UserId, ct: cts.Token))
            {
                conversationId = convId;
                if (++received == 2)
                    await cts.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        await ShouldHoldMessagesAsync(conversationId, ("user", "Hi"), ("assistant", "Hello"));
    }

    [Fact]
    public async Task StreamResponseAsync_ProviderFailsBeforeAnyToken_SavesNoReply()
    {
        _aiProvider.Tokens = [];
        _aiProvider.ThrowAfterTokens = new InvalidOperationException("model failed");

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Hi", UserId)) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Conversations.Single().Messages.Select(m => m.Role).Should().Equal(MessageRole.User);
    }

    // ── Titles ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamResponseAsync_ANewConversation_IsTitledWithItsFirstMessage()
    {
        _titles.Title = null;
        var message = "  What is\n  a monad, and why does everyone keep talking about them so much? ";

        var conversationId = await StartConversationAsync(message, UserId);

        _repository.Find(conversationId).Title.Should().Be(ConversationTitle.From(message));
    }

    [Fact]
    public async Task StreamResponseAsync_AfterTheFirstReply_StoresTheGeneratedTitle()
    {
        _titles.Title = "Monads Explained";

        var conversationId = await StartConversationAsync("What is a monad?", UserId, model: "groq");

        _titles.Calls.Should().ContainSingle().Which.Should().Be(("groq", "What is a monad?"));
        _repository.Find(conversationId).Title.Should().Be("Monads Explained");
    }

    [Fact]
    public async Task StreamResponseAsync_AsksForTheTitleOnlyAfterTheLastToken()
    {
        _aiProvider.Tokens = ["One", "Two"];
        var tokensBeforeTitle = -1;
        var received = 0;
        _titles.OnGenerate = () => tokensBeforeTitle = received;

        await foreach (var _ in _sut.StreamResponseAsync("Hi", UserId))
            received++;

        tokensBeforeTitle.Should().Be(2);
    }

    [Fact]
    public async Task StreamResponseAsync_WhenNoTitleIsGenerated_KeepsTheFirstMessageTitle()
    {
        _titles.Title = null;

        var conversationId = await StartConversationAsync("What is a monad?", UserId);

        _repository.Find(conversationId).Title.Should().Be("What is a monad?");
    }

    [Fact]
    public async Task StreamResponseAsync_ALaterReply_DoesNotAskForATitle()
    {
        var conversationId = await StartConversationAsync("First", UserId);
        _titles.Calls.Clear();
        _titles.Title = "Something else";

        await foreach (var _ in _sut.StreamResponseAsync("Second", UserId, conversationId)) { }

        _titles.Calls.Should().BeEmpty();
        _repository.Find(conversationId).Title.Should().Be("Generated title");
    }

    [Fact]
    public async Task StreamResponseAsync_AFirstReplyThatFailsPartWay_IsNotTitled_NorIsALaterOne()
    {
        _aiProvider.Tokens = ["Hel"];
        _aiProvider.ThrowAfterTokens = new InvalidOperationException("model failed");
        Guid conversationId = Guid.Empty;
        var act = async () =>
        {
            await foreach (var (id, _) in _sut.StreamResponseAsync("First", UserId))
                conversationId = id;
        };
        await act.Should().ThrowAsync<InvalidOperationException>();

        _aiProvider.ThrowAfterTokens = null;
        await foreach (var _ in _sut.StreamResponseAsync("Second", UserId, conversationId)) { }

        _titles.Calls.Should().BeEmpty();
        _repository.Find(conversationId).Title.Should().Be("First");
    }

    [Fact]
    public async Task StreamResponseAsync_AFirstReplyCancelledPartWay_IsNotTitled()
    {
        _aiProvider.Tokens = ["Hel"];
        _aiProvider.WaitForCancellationAfterTokens = true;
        using var cts = new CancellationTokenSource();
        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Hi", UserId, ct: cts.Token))
                await cts.CancelAsync();
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        _titles.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamResponseAsync_AnEmptyFirstReply_DoesNotAskForATitle()
    {
        _aiProvider.Tokens = [];

        await StartConversationAsync("First", UserId);

        _titles.Calls.Should().BeEmpty();
        _repository.Conversations.Single().Title.Should().Be("First");
    }

    [Fact]
    public async Task StreamResponseAsync_WhenTheTitleCannotBeSaved_StillEndsTheReply()
    {
        _repository.SetTitleFails = true;

        var conversationId = await StartConversationAsync("First", UserId);

        _repository.Find(conversationId).Title.Should().Be("First");
        await ShouldHoldMessagesAsync(conversationId, ("user", "First"), ("assistant", "AI response"));
    }

    [Fact]
    public async Task StreamResponseAsync_ACallerWhoLeavesWhileTheTitleIsGenerated_StillGetsItStored()
    {
        using var cts = new CancellationTokenSource();
        _titles.OnGenerate = cts.Cancel;
        Guid conversationId = Guid.Empty;

        await foreach (var (id, _) in _sut.StreamResponseAsync("Hi", UserId, ct: cts.Token))
            conversationId = id;

        _titles.ReceivedToken.CanBeCanceled.Should().BeFalse();
        _repository.Find(conversationId).Title.Should().Be("Generated title");
    }

    [Fact]
    public async Task StreamResponseAsync_ATitledNewConversation_CountsOneMessage()
    {
        await StartConversationAsync("Hi", UserId);

        _quota.Consumed.Should().Be(1);
    }

    [Fact]
    public async Task StreamResponseAsync_ATitledConversation_HoldsOnlyTheMessageAndTheReply()
    {
        var conversationId = await StartConversationAsync("Hi", UserId);

        await ShouldHoldMessagesAsync(conversationId, ("user", "Hi"), ("assistant", "AI response"));
    }

    // ── GetHistoryAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistoryAsync_WithValidId_ReturnsHistory()
    {
        var sent = await StartConversationAsync("Hello", UserId);

        var history = await _sut.GetHistoryAsync(sent, UserId);

        history.Should().NotBeNull();
        history!.ConversationId.Should().Be(sent);
    }

    [Fact]
    public async Task GetHistoryAsync_WithInvalidId_ReturnsNull()
    {
        var history = await _sut.GetHistoryAsync(Guid.NewGuid(), UserId);

        history.Should().BeNull();
    }

    // ── Ownership ───────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamResponseAsync_IntoAnotherUsersConversation_ThrowsWithoutCallingAiOrCreating()
    {
        var owners = await StartConversationAsync("Mine", UserId);
        _aiProvider.ReceivedHistory = null;

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Intruder", OtherUserId, owners)) { }
        };

        await act.Should().ThrowAsync<ConversationNotFoundException>();
        _aiProvider.ReceivedHistory.Should().BeNull();
        _repository.ConversationCount.Should().Be(1);
    }

    // ── The daily message limit ─────────────────────────────────────────────

    [Fact]
    public async Task StreamResponseAsync_OverTheDailyLimit_SavesNothingAndAsksNoModel()
    {
        _quota.Allow = false;

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Hi", UserId)) { }
        };

        var thrown = await act.Should().ThrowAsync<DailyMessageLimitReachedException>();
        thrown.Which.Limit.Should().Be(FakeQuota.LimitValue);
        _repository.ConversationCount.Should().Be(0, "a refused first message must leave no empty conversation");
        _aiProvider.ReceivedHistory.Should().BeNull();
    }

    [Fact]
    public async Task StreamResponseAsync_OverTheDailyLimit_InAnExistingConversation_SavesNothing()
    {
        var conversationId = await StartConversationAsync("Hello", UserId);
        var before = (await _sut.GetHistoryAsync(conversationId, UserId))!.Messages.Count;
        _quota.Allow = false;

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Again", UserId, conversationId)) { }
        };

        await act.Should().ThrowAsync<DailyMessageLimitReachedException>();
        (await _sut.GetHistoryAsync(conversationId, UserId))!.Messages.Should().HaveCount(before);
    }

    [Fact]
    public async Task StreamResponseAsync_IntoAnotherUsersConversation_DoesNotCountTowardsTheLimit()
    {
        var owners = await StartConversationAsync("Mine", UserId);
        var consumedBefore = _quota.Consumed;

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Intruder", OtherUserId, owners)) { }
        };

        await act.Should().ThrowAsync<ConversationNotFoundException>();
        _quota.Consumed.Should().Be(consumedBefore);
    }

    [Fact]
    public async Task StreamResponseAsync_AnAcceptedMessage_CountsOnce()
    {
        await StartConversationAsync("Hello", UserId);

        _quota.Consumed.Should().Be(1);
    }

    [Fact]
    public async Task StreamResponseAsync_AMessageThatFailsAtTheModel_StillCounts()
    {
        _aiProvider.ThrowAfterTokens = new InvalidOperationException("model down");
        _aiProvider.Tokens = [];

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Hi", UserId)) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();
        _quota.Consumed.Should().Be(1);
    }

    [Fact]
    public async Task StreamResponseAsync_PassesTheModelOptionToTheProvider()
    {
        await foreach (var _ in _sut.StreamResponseAsync("Hi", UserId, modelId: "groq")) { }

        _aiProvider.ReceivedModelId.Should().Be("groq");
    }

    [Fact]
    public async Task GetHistoryAsync_ForAnotherUsersConversation_ReturnsNull()
    {
        var owners = await StartConversationAsync("Mine", UserId);

        var history = await _sut.GetHistoryAsync(owners, OtherUserId);

        history.Should().BeNull();
    }

    [Fact]
    public async Task StreamResponseAsync_WithEmptyUserId_ThrowsArgumentException()
    {
        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Hello", Guid.Empty)) { }
        };

        await act.Should().ThrowAsync<ArgumentException>();
        _repository.ConversationCount.Should().Be(0);
    }

    [Fact]
    public async Task StreamResponseAsync_WithEmptyUserIdAndExistingConversation_ThrowsArgumentException()
    {
        var owners = await StartConversationAsync("Mine", UserId);

        var act = async () =>
        {
            await foreach (var _ in _sut.StreamResponseAsync("Hello", Guid.Empty, owners)) { }
        };

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetHistoryAsync_WithEmptyUserId_ThrowsArgumentException()
    {
        var act = () => _sut.GetHistoryAsync(Guid.NewGuid(), Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private async Task ShouldHoldMessagesAsync(Guid conversationId, params (string Role, string Content)[] expected)
    {
        var history = await _sut.GetHistoryAsync(conversationId, UserId);
        history!.Messages.Select(m => (m.Role, m.Content)).Should().Equal(expected);
    }

    private async Task<Guid> StartConversationAsync(string content, Guid userId, string? model = null)
    {
        Guid conversationId = Guid.Empty;
        await foreach (var (convId, _) in _sut.StreamResponseAsync(content, userId, modelId: model))
            conversationId = convId;
        return conversationId;
    }

    // ── test doubles ────────────────────────────────────────────────────────

    private sealed class FakeAiProvider : IAiProvider
    {
        public IEnumerable<string> Tokens { get; set; } = ["AI response"];
        public IReadOnlyList<ChatMessage>? ReceivedHistory { get; set; }
        public string? ReceivedModelId { get; set; }
        public Exception? ThrowAfterTokens { get; set; }
        public bool WaitForCancellationAfterTokens { get; set; }

        // The service asks ITitleGenerator for titles, never the provider directly.
        public Task<string> CompleteAsync(string? modelId, string instructions, string content, int maxTokens, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<string> StreamCompletionAsync(
            string? modelId,
            IReadOnlyList<ChatMessage> history,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ReceivedModelId = modelId;
            ReceivedHistory = history;
            foreach (var token in Tokens)
            {
                yield return token;
                await Task.Yield();
            }

            if (ThrowAfterTokens is not null)
                throw ThrowAfterTokens;
            if (WaitForCancellationAfterTokens)
                await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class FakeQuota : IDailyMessageQuota
    {
        public const int LimitValue = 7;

        public bool Allow { get; set; } = true;
        public int Consumed { get; private set; }
        public int Limit => LimitValue;

        public Task<bool> TryConsumeAsync(Guid userId, CancellationToken ct = default)
        {
            Consumed++;
            return Task.FromResult(Allow);
        }
    }

    private sealed class FakeTitleGenerator : ITitleGenerator
    {
        public string? Title { get; set; } = "Generated title";
        public List<(string? ModelId, string FirstMessage)> Calls { get; } = [];
        public CancellationToken ReceivedToken { get; private set; }
        public Action? OnGenerate { get; set; }

        public Task<string?> GenerateAsync(string? modelId, string firstMessage, CancellationToken ct = default)
        {
            Calls.Add((modelId, firstMessage));
            ReceivedToken = ct;
            OnGenerate?.Invoke();
            return Task.FromResult(Title);
        }
    }

    /// <summary>Mirrors the repository contract: lookups match on id and owner together.</summary>
    private sealed class FakeChatRepository : IChatRepository
    {
        private readonly Dictionary<Guid, Conversation> _store = new();

        public int ConversationCount => _store.Count;
        public IEnumerable<Conversation> Conversations => _store.Values;

        public Task<Conversation> CreateConversationAsync(Guid userId, string title, CancellationToken ct = default)
        {
            var conversation = new Conversation(userId, title);
            _store[conversation.Id] = conversation;
            return Task.FromResult(conversation);
        }

        public Task<Conversation?> GetConversationAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(FindOwned(conversationId, userId));

        public Task AddMessageAsync(Guid conversationId, Guid userId, ChatMessage message, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var conversation = FindOwned(conversationId, userId)
                ?? throw new ConversationNotFoundException(conversationId);
            conversation.AddMessage(message);
            return Task.CompletedTask;
        }

        public bool SetTitleFails { get; set; }

        public Task SetTitleAsync(Guid conversationId, Guid userId, string title, CancellationToken ct = default)
        {
            if (SetTitleFails)
                throw new InvalidOperationException("database unavailable");
            var conversation = FindOwned(conversationId, userId)
                ?? throw new ConversationNotFoundException(conversationId);
            conversation.ChangeTitle(title);
            return Task.CompletedTask;
        }

        public ConversationPage Page { get; } = new([], null);
        public (Guid UserId, int Limit, string? Cursor)? ListedWith { get; private set; }

        public Task<ConversationPage> ListConversationsAsync(Guid userId, int limit, string? cursor, CancellationToken ct = default)
        {
            ListedWith = (userId, limit, cursor);
            return Task.FromResult(Page);
        }

        public Conversation Find(Guid conversationId) => _store[conversationId];

        private Conversation? FindOwned(Guid conversationId, Guid userId) =>
            _store.TryGetValue(conversationId, out var conversation) && conversation.OwnerId == userId
                ? conversation
                : null;
    }
}
