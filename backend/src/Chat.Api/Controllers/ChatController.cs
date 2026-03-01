using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

/// <summary>
/// Handles chat message requests and conversation history.
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
    /// Sends a chat message. Creates a new conversation when ConversationId is omitted.
    /// </summary>
    /// <param name="request">The chat request. Include ConversationId to continue an existing conversation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Echo response with ConversationId.</returns>
    /// <response code="200">Returns the echo response with ConversationId.</response>
    /// <response code="400">If the message is null, empty, or exceeds 5000 characters.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChatResponseDto>> SendMessage(
        [FromBody] ChatRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be null, empty, or whitespace.");

        if (request.Message.Length > ChatMessage.MaxContentLength)
            return BadRequest($"Message cannot exceed {ChatMessage.MaxContentLength} characters.");

        var response = await _chatService.SendMessageAsync(
            request.Message,
            request.ConversationId,
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the full message history for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>All messages in chronological order.</returns>
    /// <response code="200">Returns the conversation history.</response>
    /// <response code="404">If the conversation does not exist.</response>
    [HttpGet("{conversationId:guid}/history")]
    [ProducesResponseType(typeof(ConversationHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationHistoryDto>> GetHistory(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var history = await _chatService.GetHistoryAsync(conversationId, cancellationToken);

        if (history is null)
            return NotFound();

        return Ok(history);
    }
}
