using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Chat.Api.Tests.Infrastructure;
using Chat.Application.DTOs;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Tests.Controllers;

public class ChatStreamingTests : IClassFixture<ChatApiFactory>
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly ChatApiFactory _factory;
    private readonly HttpClient _client;

    public ChatStreamingTests(ChatApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── A reply streams as conversation id, tokens, then an end marker ──────

    [Fact]
    public async Task PostMessage_WithoutConversationId_StreamsIdThenTokensThenDone()
    {
        using var response = await EventStream.PostMessageAsync(_client, new { message = "Hello" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var events = await EventStream.ReadAllEventsAsync(response);
        events.Select(e => e.Event).Should().Equal("conversation", "token", "token", "token", "done");
        events[0].Json<Guid>().Should().NotBeEmpty();
        events.Where(e => e.Event == "token").Select(e => e.Json<string>())
            .Should().Equal("Fake", " AI", " response");
    }

    [Fact]
    public async Task PostMessage_WithoutConversationId_StartsAConversationOwnedByTheSender()
    {
        using var response = await EventStream.PostMessageAsync(_client, new { message = "Hello" }, user: Alice);
        var events = await EventStream.ReadAllEventsAsync(response);

        var conversationId = events.First(e => e.Event == "conversation").Json<Guid>();
        (await _factory.Repository.GetConversationAsync(conversationId, Alice)).Should().NotBeNull();
        (await _factory.Repository.GetConversationAsync(conversationId, Bob)).Should().BeNull();
    }

    [Fact]
    public async Task PostMessage_WhenDone_SavesTheStreamedTokensAsTheReply()
    {
        using var response = await EventStream.PostMessageAsync(_client, new { message = "Hello" });
        var events = await EventStream.ReadAllEventsAsync(response);
        var conversationId = events.First(e => e.Event == "conversation").Json<Guid>();
        var streamed = string.Concat(events.Where(e => e.Event == "token").Select(e => e.Json<string>()));

        var history = await _client.GetFromJsonAsync<ConversationHistoryDto>($"/api/chat/{conversationId}/history");

        history!.Messages.Select(m => (m.Role, m.Content)).Should().Equal(
            ("user", "Hello"), ("assistant", streamed));
    }

    [Fact]
    public async Task PostMessage_TokenWithALineBreak_ArrivesAsOneTokenIntact()
    {
        await using var factory = new ChatApiFactory();
        factory.Ai.Script = Tokens("Hel", "lo\nworld");

        using var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" });
        var events = await EventStream.ReadAllEventsAsync(response);

        events.Where(e => e.Event == "token").Select(e => e.Json<string>())
            .Should().Equal("Hel", "lo\nworld");
    }

    [Fact]
    public async Task PostMessage_IntoOwnConversation_StreamsItsIdAndAddsToIt()
    {
        var conversation = await _factory.Repository.CreateConversationAsync(Alice);
        await _factory.Repository.AddMessageAsync(conversation.Id, Alice, new ChatMessage("First", MessageRole.User));
        await _factory.Repository.AddMessageAsync(conversation.Id, Alice, new ChatMessage("Earlier reply", MessageRole.Assistant));

        using var response = await EventStream.PostMessageAsync(
            _client, new { message = "Second", conversationId = conversation.Id }, user: Alice);
        var events = await EventStream.ReadAllEventsAsync(response);

        events.First(e => e.Event == "conversation").Json<Guid>().Should().Be(conversation.Id);
        var stored = await _factory.Repository.GetConversationAsync(conversation.Id, Alice);
        stored!.Messages.Select(m => m.Content).Should().Equal("First", "Earlier reply", "Second", "Fake AI response");
    }

    // ── Refusals are answered before the stream starts ──────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task PostMessage_EmptyIntoAnotherUsersConversation_Returns400AndLeavesItUnchanged(string? message)
    {
        var alices = await StartConversationAs(Alice);
        var aiCallsBefore = _factory.Ai.Calls;
        // null stands for a body with no message field at all
        object body = message is null
            ? new { conversationId = alices }
            : new { message, conversationId = alices };

        using var response = await EventStream.PostMessageAsync(_client, body, user: Bob);

        await ShouldBeRefused(response, HttpStatusCode.BadRequest, "Message cannot be empty.");
        _factory.Ai.Calls.Should().Be(aiCallsBefore);
        await ShouldHoldOnlyItsFirstExchange(alices);
    }

    [Fact]
    public async Task PostMessage_OverLongIntoAnotherUsersConversation_Returns400AndLeavesItUnchanged()
    {
        var alices = await StartConversationAs(Alice);
        var aiCallsBefore = _factory.Ai.Calls;
        var tooLong = new string('x', ChatMessage.MaxContentLength + 1);

        using var response = await EventStream.PostMessageAsync(
            _client, new { message = tooLong, conversationId = alices }, user: Bob);

        await ShouldBeRefused(response, HttpStatusCode.BadRequest,
            $"Message cannot be longer than {ChatMessage.MaxContentLength} characters.");
        _factory.Ai.Calls.Should().Be(aiCallsBefore);
        await ShouldHoldOnlyItsFirstExchange(alices);
    }

    [Fact]
    public async Task PostMessage_IntoAnotherUsersConversation_Returns404AndSendsNothing()
    {
        var alices = await StartConversationAs(Alice);
        var aiCallsBefore = _factory.Ai.Calls;
        var createdBefore = _factory.Repository.ConversationsCreated;

        using var response = await EventStream.PostMessageAsync(
            _client, new { message = "Intruder", conversationId = alices }, user: Bob);

        await ShouldBeRefused(response, HttpStatusCode.NotFound, "Conversation not found.");
        _factory.Ai.Calls.Should().Be(aiCallsBefore);
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
        await ShouldHoldOnlyItsFirstExchange(alices);
    }

    [Fact]
    public async Task PostMessage_IntoUnknownConversation_IsRefusedExactlyLikeAnotherUsers()
    {
        var alices = await StartConversationAs(Alice);
        var aiCallsBefore = _factory.Ai.Calls;
        var createdBefore = _factory.Repository.ConversationsCreated;

        using var someoneElses = await EventStream.PostMessageAsync(
            _client, new { message = "Hello", conversationId = alices }, user: Bob);
        using var unknown = await EventStream.PostMessageAsync(
            _client, new { message = "Hello", conversationId = Guid.NewGuid() }, user: Bob);

        var expected = await ShouldBeRefused(someoneElses, HttpStatusCode.NotFound, "Conversation not found.");
        var actual = await ShouldBeRefused(unknown, HttpStatusCode.NotFound, "Conversation not found.");
        // Everything but the per-request trace id must match.
        (actual.Status, actual.Title, actual.Type, actual.Detail)
            .Should().Be((expected.Status, expected.Title, expected.Type, expected.Detail));
        _factory.Ai.Calls.Should().Be(aiCallsBefore);
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
    }

    [Fact]
    public async Task PostMessage_WhenPlanExcludesChat_Returns403WithPlanErrorAndReachesNoModel()
    {
        await using var factory = new DisabledChatFeatureFactory();

        using var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" });

        await ShouldBeRefused(response, HttpStatusCode.Forbidden, "Chat is not available on your current plan.");
        factory.Ai.Calls.Should().Be(0);
        factory.Repository.ConversationsCreated.Should().Be(0);
    }

    // ── Streaming behaviour ─────────────────────────────────────────────────

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PostMessage_FirstToken_ArrivesBeforeTheModelProducesTheNext()
    {
        await using var factory = new ChatApiFactory();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Ai.Script = ct => HoldAfterFirstToken(release.Task, ct);
        try
        {
            using var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" })
                .WaitAsync(Patience);
            await using var events = EventStream.ReadEventsAsync(response).GetAsyncEnumerator();

            // The model is still holding its second token, so a buffered stream would time out here.
            (await events.MoveNextAsync().AsTask().WaitAsync(Patience)).Should().BeTrue();
            events.Current.Event.Should().Be("conversation");
            (await events.MoveNextAsync().AsTask().WaitAsync(Patience)).Should().BeTrue();
            events.Current.Should().Be(new ServerSentEvent("token", "\"Hel\""));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task PostMessage_ModelFailsAfterFirstToken_EndsWithErrorEventAndSavesThePartialReply()
    {
        await using var factory = new ChatApiFactory();
        factory.Ai.Script = ct => FailAfter(["Hel"], ct);

        using var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" });
        var events = await EventStream.ReadAllEventsAsync(response);

        events.Select(e => e.Event).Should().Equal("conversation", "token", "error");
        events[2].Json<ErrorEvent>().Message.Should().Be("The reply could not be completed.");
        events[2].Data.Should().NotContain(InternalDetail);
        var stored = await factory.Repository.GetConversationAsync(events[0].Json<Guid>(), TestAuthHandler.TestUserId);
        stored!.Messages.Select(m => m.Content).Should().Equal("Hello", "Hel");
    }

    // Runs as Development, where an escaped exception would show its text on the error page.
    [Fact]
    public async Task PostMessage_ModelFailsBeforeFirstToken_Returns502WithoutDetailOrStream()
    {
        await using var factory = new ChatApiFactory();
        factory.Ai.Script = ct => FailAfter([], ct);

        using var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" });

        await ShouldBeRefused(response, HttpStatusCode.BadGateway, "The reply could not be completed.");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(InternalDetail);
        factory.Repository.RepliesAdded.Should().Be(0);
    }

    [Fact]
    public async Task PostMessage_CallerClosesPartWay_StopsTheModelAndSavesThePartialReply()
    {
        await using var factory = new ChatApiFactory();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Ai.Script = ct => FirstTokenThenWaitForCancellation(cancelled, ct);

        var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" });
        var events = EventStream.ReadEventsAsync(response).GetAsyncEnumerator();
        (await events.MoveNextAsync().AsTask().WaitAsync(Patience)).Should().BeTrue();
        var conversationId = events.Current.Json<Guid>();
        (await events.MoveNextAsync().AsTask().WaitAsync(Patience)).Should().BeTrue();
        events.Current.Event.Should().Be("token");

        await events.DisposeAsync();
        response.Dispose();

        await cancelled.Task.WaitAsync(Patience);
        // The save follows the model stopping, so wait for it rather than read once.
        await WaitForMessageCountAsync(factory, conversationId, 2);
        var stored = await factory.Repository.GetConversationAsync(conversationId, TestAuthHandler.TestUserId);
        stored!.Messages.Select(m => m.Content).Should().Equal("Hello", "Hel");
    }

    private static async Task WaitForMessageCountAsync(ChatApiFactory factory, Guid conversationId, int count)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            var stored = await factory.Repository.GetConversationAsync(conversationId, TestAuthHandler.TestUserId);
            if (stored!.Messages.Count >= count) return;
            await Task.Delay(20);
        }
    }

    private const string InternalDetail = "secret internal detail";

    private record ErrorEvent(string Message);

    private static async IAsyncEnumerable<string> HoldAfterFirstToken(
        Task release, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return "Hel";
        await release.WaitAsync(ct);
        yield return "lo";
    }

    private static async IAsyncEnumerable<string> FailAfter(
        string[] tokens, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var token in tokens)
            yield return token;
        await Task.Yield();
        throw new InvalidOperationException(InternalDetail);
    }

    private static async IAsyncEnumerable<string> FirstTokenThenWaitForCancellation(
        TaskCompletionSource cancelled, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return "Hel";
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            cancelled.TrySetResult();
            throw;
        }
        yield return "never";
    }

    [Fact]
    public async Task PostMessage_ChecksThePlanOfTheSignedInCaller()
    {
        await using var factory = new RecordingPlanFeatureFactory();

        using var response = await EventStream.PostMessageAsync(factory.CreateClient(), new { message = "Hello" }, user: Alice);
        await EventStream.ReadAllEventsAsync(response);

        factory.PlanService.CheckedUserIds.Should().Equal(Alice);
    }

    private async Task<Guid> StartConversationAs(Guid user)
    {
        var conversation = await _factory.Repository.CreateConversationAsync(user);
        await _factory.Repository.AddMessageAsync(conversation.Id, user, new ChatMessage("Mine", MessageRole.User));
        await _factory.Repository.AddMessageAsync(conversation.Id, user, new ChatMessage("Earlier reply", MessageRole.Assistant));
        return conversation.Id;
    }

    private async Task ShouldHoldOnlyItsFirstExchange(Guid alices)
    {
        var stored = await _factory.Repository.GetConversationAsync(alices, Alice);
        stored!.Messages.Select(m => m.Content).Should().Equal("Mine", "Earlier reply");
    }

    private static async Task<ProblemDetails> ShouldBeRefused(
        HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        await response.Content.LoadIntoBufferAsync();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Be(detail);
        return problem;
    }

    private static Func<CancellationToken, IAsyncEnumerable<string>> Tokens(params string[] tokens) =>
        ct => Yield(tokens, ct);

    private static async IAsyncEnumerable<string> Yield(
        string[] tokens, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var token in tokens)
        {
            yield return token;
            await Task.Yield();
        }
    }
}
