using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Infrastructure.AI;
using Chat.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using DomainMessage = Chat.Domain.Entities.ChatMessage;

namespace Chat.Infrastructure.Tests.AI;

/// <summary>
/// The generator asks the model that answered for a title and never lets a failure escape, since
/// a title must never affect a reply.
/// </summary>
public class ModelTitleGeneratorTests
{
    private static readonly AiSettings Settings = new() { DefaultModel = "a", MaxTitleTokens = 77 };

    private static ModelTitleGenerator Generator(FakeProvider provider, TimeSpan? timeout = null) =>
        new(provider, Settings, NullLogger<ModelTitleGenerator>.Instance, timeout ?? TimeSpan.FromSeconds(10));

    [Fact]
    public async Task AsksTheGivenModel_WithTheTitleInstruction_TheFirstMessage_AndTheTitleCap()
    {
        var provider = new FakeProvider { Answer = "Monads" };

        await Generator(provider).GenerateAsync("b", "What is a monad?");

        provider.ModelId.Should().Be("b");
        provider.Instructions.Should().Contain("title");
        provider.Content.Should().Be("What is a monad?");
        provider.MaxTokens.Should().Be(77);
    }

    [Fact]
    public async Task SendsAtMostTheFirstThousandCharactersOfTheMessage()
    {
        var provider = new FakeProvider { Answer = "Long" };

        await Generator(provider).GenerateAsync("a", new string('x', 4000));

        provider.Content.Should().Be(new string('x', 1000));
    }

    [Fact]
    public async Task CleansTheAnswer()
    {
        var provider = new FakeProvider { Answer = "\"Understanding Monads\"\n" };

        var title = await Generator(provider).GenerateAsync("a", "What is a monad?");

        title.Should().Be("Understanding Monads");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    [InlineData("\"\"")]
    public async Task NothingLeftAfterCleaning_GivesNull(string answer)
    {
        var title = await Generator(new FakeProvider { Answer = answer }).GenerateAsync("a", "Hi");

        title.Should().BeNull();
    }

    [Fact]
    public async Task ARateLimit_GivesNull()
    {
        var provider = new FakeProvider { Failure = new ModelRateLimitedException("slow down") };

        (await Generator(provider).GenerateAsync("a", "Hi")).Should().BeNull();
    }

    [Fact]
    public async Task AnyOtherFailure_GivesNull()
    {
        var provider = new FakeProvider { Failure = new InvalidOperationException("boom") };

        (await Generator(provider).GenerateAsync("a", "Hi")).Should().BeNull();
    }

    [Fact]
    public async Task NoAnswerBeforeTheTimeout_GivesNull_AndCancelsTheRequest()
    {
        var provider = new FakeProvider { NeverAnswers = true };

        var title = await Generator(provider, TimeSpan.FromMilliseconds(50)).GenerateAsync("a", "Hi");

        title.Should().BeNull();
        provider.WasCancelled.Should().BeTrue();
    }

    private sealed class FakeProvider : IAiProvider
    {
        public string Answer { get; init; } = string.Empty;
        public Exception? Failure { get; init; }
        public bool NeverAnswers { get; init; }

        public string? ModelId { get; private set; }
        public string? Instructions { get; private set; }
        public string? Content { get; private set; }
        public int MaxTokens { get; private set; }
        public bool WasCancelled { get; private set; }

        public async Task<string> CompleteAsync(
            string? modelId, string instructions, string content, int maxTokens, CancellationToken ct = default)
        {
            (ModelId, Instructions, Content, MaxTokens) = (modelId, instructions, content, maxTokens);
            if (Failure is not null)
                throw Failure;
            if (NeverAnswers)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    throw;
                }
            }
            return Answer;
        }

        public IAsyncEnumerable<string> StreamCompletionAsync(
            string? modelId, IReadOnlyList<DomainMessage> history, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
