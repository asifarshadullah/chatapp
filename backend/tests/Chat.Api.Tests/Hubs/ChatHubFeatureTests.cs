using Chat.Api.Tests.Infrastructure;
using Chat.Billing.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Hubs;

/// <summary>
/// Integration tests for ChatHub feature-gating via IPlanFeatureService.
/// </summary>
public class ChatHubFeatureTests
{
    // ── Cycle 4.1 — feature disabled → HubException ──────────────────────────

    [Fact]
    public async Task SendMessage_WhenChatFeatureDisabled_ThrowsHubException()
    {
        await using var factory = new DisabledChatFeatureFactory();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "chatHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        var act = async () =>
        {
            await foreach (var _ in connection.StreamAsync<string>("SendMessage", "Hello", (Guid?)null))
            { /* drain */ }
        };

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*plan*");

        await connection.StopAsync();
    }

    // ── Cycle 4.2 — feature enabled → streams response ───────────────────────

    [Fact]
    public async Task SendMessage_WhenChatFeatureEnabled_StreamsResponse()
    {
        await using var factory = new ChatApiFactory();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "chatHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        var tokens = new List<string>();

        await foreach (var token in connection.StreamAsync<string>("SendMessage", "Hello", (Guid?)null))
            tokens.Add(token);

        tokens.Should().NotBeEmpty();

        await connection.StopAsync();
    }
}

/// <summary>
/// ChatApiFactory variant that replaces IPlanFeatureService with a stub that always returns false,
/// simulating a plan that has the chat feature disabled.
/// </summary>
public class DisabledChatFeatureFactory : ChatApiFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPlanFeatureService));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IPlanFeatureService, AlwaysDisablePlanFeatureService>();
        });
    }
}

/// <summary>Stub that disables every feature — simulates a locked-down plan.</summary>
public class AlwaysDisablePlanFeatureService : IPlanFeatureService
{
    public Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default)
        => Task.FromResult(false);
}
