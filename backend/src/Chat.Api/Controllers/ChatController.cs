using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

/// <summary>
/// Handles chat message requests.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatController"/> class.
    /// </summary>
    /// <param name="chatService">The chat service for processing messages.</param>
    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Sends a chat message and receives an echo response.
    /// </summary>
    /// <param name="request">The chat request containing the user's message.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ChatResponseDto"/> with the echo response.</returns>
    /// <response code="200">Returns the echo response.</response>
    /// <response code="400">If the message is null, empty, or exceeds 5000 characters.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChatResponseDto>> SendMessage(
        [FromBody] ChatRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("Message cannot be null, empty, or whitespace.");
        }

        if (request.Message.Length > ChatMessage.MaxContentLength)
        {
            return BadRequest($"Message cannot exceed {ChatMessage.MaxContentLength} characters.");
        }

        var response = await _chatService.SendMessageAsync(request.Message, cancellationToken);

        return Ok(response);
    }
}
