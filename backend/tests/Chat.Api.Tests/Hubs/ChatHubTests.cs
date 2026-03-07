using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.Api.Tests.Hubs;

/// <summary>
/// Integration tests for ChatHub using InMemoryChatRepository (no Docker needed).
/// </summary>
public class ChatHubTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;

    public ChatHubTests(ChatApiFactory factory)
    {
        _factory = factory;
    }

    private HubConnection CreateHubConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "chatHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    // ── Cycle 1.4 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_StreamsEchoResponseWordByWord()
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        try
        {
            var received = new List<string>();
            await foreach (var word in connection.StreamAsync<string>(
                "SendMessage", "Hello World", (Guid?)null, CancellationToken.None))
            {
                received.Add(word);
            }

            string.Join("", received).Trim().Should().Be("Echo: Hello World");
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    // ── Cycle 1.5 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_StoresMessagesInConversation()
    {
        var connection = CreateHubConnection();
        var conversationIdSource = new TaskCompletionSource<Guid>();
        connection.On<Guid>("ReceiveConversationId", id => conversationIdSource.TrySetResult(id));
        await connection.StartAsync();
        try
        {
            await foreach (var _ in connection.StreamAsync<string>(
                "SendMessage", "Stored message", (Guid?)null, CancellationToken.None)) { }

            var conversationId = await conversationIdSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var client = _factory.CreateClient();
            var response = await client.GetAsync($"/api/chat/{conversationId}/history");
            response.IsSuccessStatusCode.Should().BeTrue();

            var history = await response.Content.ReadFromJsonAsync<ConversationHistoryDto>();
            history!.Messages.Should().HaveCount(2);
            history.Messages.Should().ContainSingle(m =>
                m.Role == "user" && m.Content == "Stored message");
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    // ── Cycle 1.6 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WithExistingConversationId_ContinuesSameConversation()
    {
        var connection = CreateHubConnection();
        Guid? firstConversationId = null;
        connection.On<Guid>("ReceiveConversationId", id => firstConversationId ??= id);
        await connection.StartAsync();
        try
        {
            await foreach (var _ in connection.StreamAsync<string>(
                "SendMessage", "First", (Guid?)null, CancellationToken.None)) { }

            firstConversationId.Should().NotBeNull();

            await foreach (var _ in connection.StreamAsync<string>(
                "SendMessage", "Second", firstConversationId, CancellationToken.None)) { }

            var client = _factory.CreateClient();
            var history = await (await client.GetAsync($"/api/chat/{firstConversationId}/history"))
                .Content.ReadFromJsonAsync<ConversationHistoryDto>();

            history!.Messages.Should().HaveCount(4); // user + echo + user + echo
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    // ── Cycle 1.7 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WithEmptyContent_ThrowsHubException()
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        try
        {
            var act = async () =>
            {
                await foreach (var _ in connection.StreamAsync<string>(
                    "SendMessage", "", (Guid?)null, CancellationToken.None)) { }
            };

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("*empty*");
        }
        finally
        {
            await connection.StopAsync();
        }
    }
}
