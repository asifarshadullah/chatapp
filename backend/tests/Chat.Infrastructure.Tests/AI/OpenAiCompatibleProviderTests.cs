using System.Net;
using System.Text.Json;
using Chat.Application.Exceptions;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.AI;
using Chat.Infrastructure.Configuration;
using FluentAssertions;

namespace Chat.Infrastructure.Tests.AI;

/// <summary>
/// The provider speaks the OpenAI chat completions API to whichever service an option names, so
/// these tests pin down the exact request it sends and how it reads the stream back.
/// </summary>
public class OpenAiCompatibleProviderTests
{
    private const string KeyA = "key-a-0123456789";
    private const string KeyB = "key-b-9876543210";

    private static AiSettings Settings(string systemPrompt = "Be brief.", int window = 10) => new()
    {
        DefaultModel = "a",
        SystemPrompt = systemPrompt,
        MaxHistoryMessages = window,
        MaxReplyTokens = 321,
        Models =
        {
            ["a"] = new() { Name = "A", BaseUrl = "https://a.example/v1", ApiKey = KeyA, Model = "model-a", ReasoningEffort = "low" },
            ["b"] = new() { Name = "B", BaseUrl = "https://b.example/openai/v1", ApiKey = KeyB, Model = "model-b" },
        },
    };

    private static OpenAiCompatibleProvider Provider(FakeChatCompletionsHandler handler, AiSettings? settings = null) =>
        new(settings ?? Settings(), new HttpClient(handler));

    private static List<ChatMessage> History(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new ChatMessage($"message {i}", i % 2 == 1 ? MessageRole.User : MessageRole.Assistant))
            .ToList();

    private static async Task<List<string>> Collect(OpenAiCompatibleProvider provider, string model,
        IReadOnlyList<ChatMessage> history)
    {
        var tokens = new List<string>();
        await foreach (var token in provider.StreamCompletionAsync(model, history))
            tokens.Add(token);
        return tokens;
    }

    private static FakeChatCompletionsHandler Answering(params string[] contents)
    {
        var handler = new FakeChatCompletionsHandler();
        handler.Release(contents.Select(FakeChatCompletionsHandler.Content).Append(FakeChatCompletionsHandler.Done).ToArray());
        handler.Finish();
        return handler;
    }

    private static JsonElement Body(FakeChatCompletionsHandler handler) =>
        JsonDocument.Parse(handler.Requests.Single().Body).RootElement;

    private static List<(string Role, string Content)> Messages(JsonElement body) =>
        body.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("role").GetString()!, m.GetProperty("content").ValueKind == JsonValueKind.String
                ? m.GetProperty("content").GetString()!
                : string.Concat(m.GetProperty("content").EnumerateArray().Select(p => p.GetProperty("text").GetString()))))
            .ToList();

    // ── Which service, which model, which key ───────────────────────────────

    [Theory]
    [InlineData("a", "https://a.example/v1/chat/completions", KeyA, "model-a")]
    [InlineData("b", "https://b.example/openai/v1/chat/completions", KeyB, "model-b")]
    public async Task AReply_GoesToTheChosenOptionsService(string option, string url, string key, string model)
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), option, History(1));

        var (request, _) = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be(url);
        request.Headers.Authorization!.ToString().Should().Be($"Bearer {key}");
        Body(handler).GetProperty("model").GetString().Should().Be(model);
        Body(handler).GetProperty("stream").GetBoolean().Should().BeTrue();
    }

    // ── What the request carries ─────────────────────────────────────────────

    [Fact]
    public async Task TheRequest_CarriesTheSystemPromptThenTheHistoryInOrder()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), "a", History(3));

        Messages(Body(handler)).Should().Equal(
            ("system", "Be brief."), ("user", "message 1"), ("assistant", "message 2"), ("user", "message 3"));
    }

    [Fact]
    public async Task WithoutASystemPrompt_TheRequestCarriesOnlyTheHistory()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler, Settings(systemPrompt: "")), "a", History(1));

        Messages(Body(handler)).Should().Equal(("user", "message 1"));
    }

    [Fact]
    public async Task ALongConversation_SendsOnlyItsLatestMessages()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), "a", History(25));

        var messages = Messages(Body(handler));
        messages.Should().HaveCount(11);
        messages[0].Should().Be(("system", "Be brief."));
        messages.Skip(1).Select(m => m.Content).Should().Equal(Enumerable.Range(16, 10).Select(i => $"message {i}"));
    }

    [Fact]
    public async Task AShortConversation_SendsEveryMessage()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), "a", History(4));

        Messages(Body(handler)).Should().HaveCount(5);
    }

    [Fact]
    public async Task TheRequest_CapsTheReplyLength()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), "a", History(1));

        var body = Body(handler);
        var cap = body.TryGetProperty("max_completion_tokens", out var completion) ? completion : body.GetProperty("max_tokens");
        cap.GetInt32().Should().Be(321);
    }

    [Fact]
    public async Task AnOptionWithAReasoningEffort_AsksForIt()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), "a", History(1));

        Body(handler).GetProperty("reasoning_effort").GetString().Should().Be("low");
    }

    [Fact]
    public async Task AnOptionWithoutAReasoningEffort_DoesNotMentionOne()
    {
        var handler = Answering("Hi");

        await Collect(Provider(handler), "b", History(1));

        Body(handler).TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    // ── Reading the stream ───────────────────────────────────────────────────

    [Fact]
    public async Task EachContentDelta_BecomesOneToken_AndReasoningNone()
    {
        var handler = new FakeChatCompletionsHandler();
        handler.Release(
            FakeChatCompletionsHandler.Reasoning("Let me think."),
            FakeChatCompletionsHandler.Content("Hel"),
            FakeChatCompletionsHandler.Content("lo"),
            FakeChatCompletionsHandler.Done);
        handler.Finish();

        var tokens = await Collect(Provider(handler), "a", History(1));

        tokens.Should().Equal("Hel", "lo");
    }

    [Fact]
    public async Task TheFirstToken_ArrivesBeforeTheStreamIsComplete()
    {
        var handler = new FakeChatCompletionsHandler();
        handler.Release(FakeChatCompletionsHandler.Content("first"));

        await using var tokens = Provider(handler).StreamCompletionAsync("a", History(1)).GetAsyncEnumerator();
        var moved = tokens.MoveNextAsync().AsTask();
        (await Task.WhenAny(moved, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(moved,
            "the first token must not wait for the rest of the stream");
        tokens.Current.Should().Be("first");

        handler.Release(FakeChatCompletionsHandler.Content("second"), FakeChatCompletionsHandler.Done);
        handler.Finish();
        (await tokens.MoveNextAsync()).Should().BeTrue();
        tokens.Current.Should().Be("second");
    }

    [Fact]
    public async Task AKeyWithALineEnding_IsSentWithoutIt()
    {
        // A secret file saved with Windows line endings keeps a \r after the binder strips \n.
        var settings = Settings();
        settings.Models["a"].ApiKey = KeyA + "\r\n";
        var handler = Answering("Hi");

        await Collect(Provider(handler, settings), "a", History(1));

        handler.Requests.Single().Request.Headers.Authorization!.ToString().Should().Be($"Bearer {KeyA}");
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARateLimitedService_IsReportedAsSuch_WithoutRetrying()
    {
        var handler = new FakeChatCompletionsHandler
        {
            Status = HttpStatusCode.TooManyRequests,
            ErrorBody = "{\"error\":{\"message\":\"Rate limit reached for key " + KeyA + "\"}}",
        };

        var act = () => Collect(Provider(handler), "a", History(1));

        var thrown = await act.Should().ThrowAsync<ModelRateLimitedException>();
        thrown.Which.ToString().Should().NotContain(KeyA);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task AFailingService_IsNotReportedAsRateLimited_AndDoesNotRevealTheKey()
    {
        // Some services echo the key they were given back in their error body.
        var handler = new FakeChatCompletionsHandler
        {
            Status = HttpStatusCode.Unauthorized,
            ErrorBody = "{\"error\":{\"message\":\"Invalid API key: " + KeyA + "\"}}",
        };

        var act = () => Collect(Provider(handler), "a", History(1));

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().NotBeOfType<ModelRateLimitedException>();
        thrown.Which.ToString().Should().NotContain(KeyA);
    }

    // ── A single completion ──────────────────────────────────────────────────

    private static async Task<(string Text, FakeChatCompletionsHandler Handler)> Complete(string option, string answer = "A title")
    {
        var handler = new FakeChatCompletionsHandler { Completion = answer };
        var text = await Provider(handler).CompleteAsync(option, "Name this.", "What is a monad?", 42);
        return (text, handler);
    }

    [Fact]
    public async Task ACompletion_IsOneUnstreamedRequest_WithOnlyTheGivenInstructionAndContent()
    {
        var (_, handler) = await Complete("a");

        var (request, _) = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be("https://a.example/v1/chat/completions");
        request.Headers.Authorization!.ToString().Should().Be($"Bearer {KeyA}");
        var body = Body(handler);
        (body.TryGetProperty("stream", out var stream) && stream.GetBoolean()).Should().BeFalse();
        body.GetProperty("model").GetString().Should().Be("model-a");
        body.GetProperty("reasoning_effort").GetString().Should().Be("low");
        Messages(body).Should().Equal(("system", "Name this."), ("user", "What is a monad?"));
        var cap = body.TryGetProperty("max_completion_tokens", out var completion) ? completion : body.GetProperty("max_tokens");
        cap.GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ACompletion_ReturnsTheAnswersText()
    {
        var (text, _) = await Complete("b", "Understanding Monads");

        text.Should().Be("Understanding Monads");
    }

    [Fact]
    public async Task ARateLimitedCompletion_IsReportedAsSuch_WithoutRetrying_OrRevealingTheKey()
    {
        var handler = new FakeChatCompletionsHandler
        {
            Status = HttpStatusCode.TooManyRequests,
            ErrorBody = "{\"error\":{\"message\":\"Rate limit reached for key " + KeyA + "\"}}",
        };

        var act = () => Provider(handler).CompleteAsync("a", "Name this.", "Hi", 42);

        var thrown = await act.Should().ThrowAsync<ModelRateLimitedException>();
        thrown.Which.ToString().Should().NotContain(KeyA);
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task AFailingCompletion_DoesNotRevealTheKey()
    {
        var handler = new FakeChatCompletionsHandler
        {
            Status = HttpStatusCode.Unauthorized,
            ErrorBody = "{\"error\":{\"message\":\"Invalid API key: " + KeyA + "\"}}",
        };

        var act = () => Provider(handler).CompleteAsync("a", "Name this.", "Hi", 42);

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().NotBeOfType<ModelRateLimitedException>();
        thrown.Which.ToString().Should().NotContain(KeyA);
    }
}
