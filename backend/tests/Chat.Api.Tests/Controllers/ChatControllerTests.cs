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
    private readonly HttpClient _client;

    public ChatControllerTests(ChatApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── POST /api/chat ──────────────────────────────────────────────────────

    [Fact]
    public async Task PostMessage_WithValidContent_ReturnsOkWithEchoResponse()
    {
        var request = new ChatRequestDto("Hello");

        var response = await _client.PostAsJsonAsync("/api/chat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ChatResponseDto>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.Message.Should().Be("Echo: Hello");
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
}
