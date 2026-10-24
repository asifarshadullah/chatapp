using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Application.DTOs;
using Chat.Domain.Entities;
using Chat.Domain.Enums;
using FluentAssertions;

namespace Chat.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the ChatController endpoint.
/// Uses ChatApiFactory (InMemoryChatRepository) so tests run without Docker.
/// </summary>
public class ChatControllerTests : IClassFixture<ChatApiFactory>
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly ChatApiFactory _factory;
    private readonly HttpClient _client;

    public ChatControllerTests(ChatApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── POST /api/chat (removed: messages are sent over the hub) ────────────

    [Fact]
    public async Task PostMessage_OverHttp_IsNotFoundAndReachesNoModel()
    {
        var createdBefore = _factory.Repository.ConversationsCreated;
        var aiCallsBefore = _factory.Ai.Calls;

        var response = await _client.PostAsJsonAsync("/api/chat", new { message = "Hello" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Ai.Calls.Should().Be(aiCallsBefore);
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
    }

    // ── GET /api/chat/{id}/history ──────────────────────────────────────────

    [Fact]
    public async Task GetHistory_WithValidId_ReturnsAllMessages()
    {
        var conversationId = await StartConversationAs(TestAuthHandler.TestUserId, "Hello");

        var response = await _client.GetAsync($"/api/chat/{conversationId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<ConversationHistoryDto>();
        history.Should().NotBeNull();
        history!.ConversationId.Should().Be(conversationId);
    }

    [Fact]
    public async Task GetHistory_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/chat/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHistory_AfterTwoMessages_ReturnsFourMessagesInOrder()
    {
        var conversationId = await StartConversationAs(TestAuthHandler.TestUserId, "First");
        await AddExchangeAs(TestAuthHandler.TestUserId, conversationId, "Second");

        var response = await _client.GetAsync($"/api/chat/{conversationId}/history");
        var history = await response.Content.ReadFromJsonAsync<ConversationHistoryDto>();

        history!.Messages.Select(m => m.Content).Should().Equal(
            "First", "Fake AI response", "Second", "Fake AI response");
    }

    // ── Ownership ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_OfAnotherUsersConversation_ReturnsNotFound()
    {
        var alices = await StartConversationAs(Alice, "Alice's secret");

        var response = await GetHistoryAs(Bob, alices);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHistory_OfAnotherUsersConversation_AsAdministrator_ReturnsNotFound()
    {
        var alices = await StartConversationAs(Alice, "Alice's secret");

        var response = await GetHistoryAs(Guid.NewGuid(), alices, role: "Admin");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Seeds straight into the repository: sending happens over the hub, not this controller.
    private async Task<Guid> StartConversationAs(Guid user, string message)
    {
        var conversation = await _factory.Repository.CreateConversationAsync(user);
        await AddExchangeAs(user, conversation.Id, message);
        return conversation.Id;
    }

    private async Task AddExchangeAs(Guid user, Guid conversationId, string message)
    {
        await _factory.Repository.AddMessageAsync(conversationId, user, new ChatMessage(message, MessageRole.User));
        await _factory.Repository.AddMessageAsync(conversationId, user, new ChatMessage("Fake AI response", MessageRole.Assistant));
    }

    private Task<HttpResponseMessage> GetHistoryAs(Guid user, Guid conversationId, string? role = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/{conversationId}/history");
        message.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        if (role is not null)
            message.Headers.Add(TestAuthHandler.RoleHeader, role);
        return _client.SendAsync(message);
    }
}
