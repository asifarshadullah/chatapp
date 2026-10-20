using System.Net;
using System.Net.Http.Json;
using Chat.Api.Tests.Infrastructure;
using Chat.Application.DTOs;
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

    // ── POST /api/chat ──────────────────────────────────────────────────────

    [Fact]
    public async Task PostMessage_WithValidContent_ReturnsOkWithAiResponse()
    {
        var request = new ChatRequestDto("Hello");

        var response = await _client.PostAsJsonAsync("/api/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ChatResponseDto>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.Message.Should().Be("Fake AI response");
        result.Role.Should().Be("assistant");
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PostMessage_WithEmptyContent_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_WithNullContent_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto(null!));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_WithContentOver5000Chars_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto(new string('a', 5001)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_ReturnsConversationIdInResponse()
    {
        var response = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto("Hello"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ChatResponseDto>();
        result!.ConversationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PostMessage_WithConversationId_ContinuesConversation()
    {
        var first = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto("Hello"));
        var firstResult = await first.Content.ReadFromJsonAsync<ChatResponseDto>();

        var second = await _client.PostAsJsonAsync("/api/chat",
            new ChatRequestDto("World", firstResult!.ConversationId));
        var secondResult = await second.Content.ReadFromJsonAsync<ChatResponseDto>();

        secondResult!.ConversationId.Should().Be(firstResult.ConversationId);
    }

    // ── GET /api/chat/{id}/history ──────────────────────────────────────────

    [Fact]
    public async Task GetHistory_WithValidId_ReturnsAllMessages()
    {
        var post = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto("Hello"));
        var posted = await post.Content.ReadFromJsonAsync<ChatResponseDto>();

        var response = await _client.GetAsync($"/api/chat/{posted!.ConversationId}/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<ConversationHistoryDto>();
        history.Should().NotBeNull();
        history!.ConversationId.Should().Be(posted.ConversationId);
    }

    [Fact]
    public async Task GetHistory_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/chat/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHistory_AfterTwoMessages_ReturnsFourMessages()
    {
        var first = await _client.PostAsJsonAsync("/api/chat", new ChatRequestDto("First"));
        var firstResult = await first.Content.ReadFromJsonAsync<ChatResponseDto>();

        await _client.PostAsJsonAsync("/api/chat",
            new ChatRequestDto("Second", firstResult!.ConversationId));

        var response = await _client.GetAsync($"/api/chat/{firstResult.ConversationId}/history");
        var history = await response.Content.ReadFromJsonAsync<ConversationHistoryDto>();

        history!.Messages.Should().HaveCount(4); // user + echo + user + echo
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

    [Fact]
    public async Task PostMessage_IntoAnotherUsersConversation_ReturnsNotFoundAndChangesNothing()
    {
        var alices = await StartConversationAs(Alice, "Alice's secret");
        var createdBefore = _factory.Repository.ConversationsCreated;
        var aiCallsBefore = _factory.Ai.Calls;

        var response = await PostAs(Bob, new ChatRequestDto("Intruder", alices));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
        _factory.Ai.Calls.Should().Be(aiCallsBefore);
        var history = await (await GetHistoryAs(Alice, alices)).Content.ReadFromJsonAsync<ConversationHistoryDto>();
        history!.Messages.Select(m => m.Content).Should().Equal("Alice's secret", "Fake AI response");
    }

    [Fact]
    public async Task PostMessage_WithUnknownConversationId_ReturnsNotFound()
    {
        var createdBefore = _factory.Repository.ConversationsCreated;

        var response = await PostAs(Bob, new ChatRequestDto("Hello", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Repository.ConversationsCreated.Should().Be(createdBefore);
    }

    [Fact]
    public async Task PostMessage_EmptyIntoAnotherUsersConversation_ReturnsBadRequest()
    {
        var alices = await StartConversationAs(Alice, "Alice's secret");

        var response = await PostAs(Bob, new ChatRequestDto("", alices));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var history = await (await GetHistoryAs(Alice, alices)).Content.ReadFromJsonAsync<ConversationHistoryDto>();
        history!.Messages.Select(m => m.Content).Should().Equal("Alice's secret", "Fake AI response");
    }

    private async Task<Guid> StartConversationAs(Guid user, string message)
    {
        var response = await PostAs(user, new ChatRequestDto(message));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ChatResponseDto>())!.ConversationId;
    }

    private Task<HttpResponseMessage> PostAs(Guid user, ChatRequestDto request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(request) };
        message.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        return _client.SendAsync(message);
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
