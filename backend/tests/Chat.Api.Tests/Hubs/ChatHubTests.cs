using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.Api.Tests.Hubs;

/// <summary>
/// Integration tests for ChatHub using InMemoryChatRepository + FakeAiProvider (no external dependencies).
/// </summary>
public class ChatHubTests : IClassFixture<ChatApiFactory>
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly ChatApiFactory _factory;

    public ChatHubTests(ChatApiFactory factory)
    {
        _factory = factory;
    }

    private HubConnection CreateHubConnection(
        Guid? user = null, HttpTransportType transport = HttpTransportType.LongPolling)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "chatHub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = transport;
                if (user is not null)
                    options.Headers[TestAuthHandler.UserHeader] = user.Value.ToString();
                if (transport == HttpTransportType.WebSockets)
                {
                    // The test server's WebSocket client does not see options.Headers, so the
                    // user header is set on its upgrade request directly.
                    options.SkipNegotiation = true;
                    options.WebSocketFactory = async (context, ct) =>
                    {
                        var client = _factory.Server.CreateWebSocketClient();
                        if (user is not null)
                            client.ConfigureRequest = r => r.Headers[TestAuthHandler.UserHeader] = user.Value.ToString();
                        return await client.ConnectAsync(context.Uri, ct);
                    };
                }
            })
            .Build();
    }

    // ── Cycle 3.1 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_StreamsAiTokens()
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        try
        {
            var received = new List<string>();
            await foreach (var token in connection.StreamAsync<string>(
                "SendMessage", "Hello", (Guid?)null, CancellationToken.None))
            {
                received.Add(token);
            }

            // FakeAiProvider yields "Fake", " AI", " response"
            string.Join("", received).Should().Be("Fake AI response");
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    // ── Cycle 3.2 ──────────────────────────────────────────────────────────

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
            history.Messages.Should().ContainSingle(m =>
                m.Role == "assistant" && m.Content == "Fake AI response");
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    // ── Cycle 3.3 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WithExistingConversationId_ContinuesSameConversation()
    {
        var connection = CreateHubConnection();
        var firstId = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid>("ReceiveConversationId", id => firstId.TrySetResult(id));
        await connection.StartAsync();
        try
        {
            await foreach (var _ in connection.StreamAsync<string>(
                "SendMessage", "First", (Guid?)null, CancellationToken.None)) { }

            // The id handler can still be pending when the stream completes, so wait for it.
            Guid? firstConversationId = await firstId.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await foreach (var _ in connection.StreamAsync<string>(
                "SendMessage", "Second", firstConversationId, CancellationToken.None)) { }

            var client = _factory.CreateClient();
            var history = await (await client.GetAsync($"/api/chat/{firstConversationId}/history"))
                .Content.ReadFromJsonAsync<ConversationHistoryDto>();

            history!.Messages.Should().HaveCount(4); // user + ai + user + ai
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    // ── Cycle 3.4 ──────────────────────────────────────────────────────────

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

    // ── Ownership ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpTransportType.LongPolling)]
    [InlineData(HttpTransportType.WebSockets)]
    public async Task SendMessage_NewConversation_BelongsToTheCaller(HttpTransportType transport)
    {
        var alices = await StartConversationAs(Alice, "Alice's secret", transport);

        var aliceReads = await GetHistoryResponseAs(Alice, alices);
        var bobReads = await GetHistoryResponseAs(Bob, alices);

        aliceReads.StatusCode.Should().Be(HttpStatusCode.OK);
        bobReads.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(HttpTransportType.LongPolling)]
    [InlineData(HttpTransportType.WebSockets)]
    public async Task SendMessage_IntoAnotherUsersConversation_FailsAndSendsNothing(HttpTransportType transport)
    {
        var alices = await StartConversationAs(Alice, "Alice's secret", transport);
        var createdBefore = _factory.Repository.ConversationsCreated;

        var (error, ids, tokens) = await SendAs(Bob, "Intruder", alices, transport);

        error.Should().NotBeNull();
        error!.Message.Should().Contain("Conversation not found.");
        ids.Should().BeEmpty();
        tokens.Should().BeEmpty();
        var history = await GetHistoryAs(Alice, alices);
        history.Messages.Select(m => m.Content).Should().Equal("Alice's secret", "Fake AI response");
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
    }

    [Fact]
    public async Task SendMessage_WithUnknownConversationId_FailsTheSameWay()
    {
        var createdBefore = _factory.Repository.ConversationsCreated;

        var (error, ids, tokens) = await SendAs(Bob, "Hello", Guid.NewGuid());

        error.Should().NotBeNull();
        error!.Message.Should().Contain("Conversation not found.");
        ids.Should().BeEmpty();
        tokens.Should().BeEmpty();
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
    }

    [Fact]
    public async Task SendMessage_EmptyIntoAnotherUsersConversation_ReportsTheEmptyMessage()
    {
        var alices = await StartConversationAs(Alice, "Alice's secret");

        var (error, _, _) = await SendAs(Bob, "", alices);

        error.Should().NotBeNull();
        error!.Message.Should().Contain("Message cannot be empty.");
        var history = await GetHistoryAs(Alice, alices);
        history.Messages.Select(m => m.Content).Should().Equal("Alice's secret", "Fake AI response");
    }

    private async Task<Guid> StartConversationAs(
        Guid user, string message, HttpTransportType transport = HttpTransportType.LongPolling)
    {
        // Waits for the id itself rather than reading it when the stream ends, so the test
        // does not depend on the id handler having run by then.
        var connection = CreateHubConnection(user, transport);
        var id = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid>("ReceiveConversationId", received => id.TrySetResult(received));
        await connection.StartAsync();
        try
        {
            await foreach (var _ in connection.StreamAsync<string>(
                "SendMessage", message, (Guid?)null, CancellationToken.None)) { }
            return await id.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    private async Task<(Exception? Error, List<Guid> Ids, List<string> Tokens)> SendAs(
        Guid user, string message, Guid? conversationId,
        HttpTransportType transport = HttpTransportType.LongPolling)
    {
        var connection = CreateHubConnection(user, transport);
        var ids = new List<Guid>();
        var tokens = new List<string>();
        connection.On<Guid>("ReceiveConversationId", id => ids.Add(id));
        await connection.StartAsync();
        try
        {
            await foreach (var token in connection.StreamAsync<string>(
                "SendMessage", message, conversationId, CancellationToken.None))
                tokens.Add(token);
            return (null, ids, tokens);
        }
        catch (Exception ex)
        {
            return (ex, ids, tokens);
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    private async Task<ConversationHistoryDto> GetHistoryAs(Guid user, Guid conversationId)
    {
        var response = await GetHistoryResponseAs(user, conversationId);
        return (await response.Content.ReadFromJsonAsync<ConversationHistoryDto>())!;
    }

    private Task<HttpResponseMessage> GetHistoryResponseAs(Guid user, Guid conversationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/{conversationId}/history");
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        return _factory.CreateClient().SendAsync(request);
    }
}
