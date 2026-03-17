using System.Runtime.CompilerServices;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that replaces MongoDB with InMemoryChatRepository
/// and Ollama with FakeAiProvider so API integration tests run without any external dependencies.
/// </summary>
public class ChatApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace MongoDB repository with fast in-memory version
            var repoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatRepository));
            if (repoDescriptor is not null)
                services.Remove(repoDescriptor);
            services.AddSingleton<IChatRepository, InMemoryChatRepository>();

            // Replace Ollama AI provider with a deterministic fake
            var aiDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiProvider));
            if (aiDescriptor is not null)
                services.Remove(aiDescriptor);
            services.AddSingleton<IAiProvider, FakeAiProvider>();
        });
    }
}

/// <summary>
/// Deterministic AI provider for integration tests.
/// Returns a fixed response so tests are fast, offline, and predictable.
/// </summary>
public class FakeAiProvider : IAiProvider
{
    public async IAsyncEnumerable<string> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "Fake";
        yield return " AI";
        yield return " response";
        await Task.Yield();
    }
}
