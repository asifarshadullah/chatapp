using System.Net;
using System.Net.Http.Json;
using Chat.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Chat.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the ChatController endpoint.
/// Uses WebApplicationFactory to spin up an in-memory test server.
/// </summary>
public class ChatControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ChatControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostMessage_WithValidContent_ReturnsOkWithEchoResponse()
    {
        // Arrange
        var request = new ChatRequestDto("Hello");

        // Act
        var response = await _client.PostAsJsonAsync("/api/chat", request);

        // Assert
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
        // Arrange
        var request = new ChatRequestDto("");

        // Act
        var response = await _client.PostAsJsonAsync("/api/chat", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_WithNullContent_ReturnsBadRequest()
    {
        // Arrange
        var request = new ChatRequestDto(null!);

        // Act
        var response = await _client.PostAsJsonAsync("/api/chat", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_WithContentOver5000Chars_ReturnsBadRequest()
    {
        // Arrange
        var longMessage = new string('a', 5001);
        var request = new ChatRequestDto(longMessage);

        // Act
        var response = await _client.PostAsJsonAsync("/api/chat", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
