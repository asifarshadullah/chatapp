using Chat.Application.DTOs;
using Chat.Application.Interfaces;
using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

/// <summary>
/// Serves conversation history. Messages are sent over the chat hub, not this controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatController"/> class.
    /// </summary>
    /// <param name="chatService">The chat service for processing messages.</param>
    /// <param name="currentUser">The signed-in caller, whose conversations this controller serves.</param>
    public ChatController(IChatService chatService, ICurrentUser currentUser)
    {
        _chatService = chatService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Returns the full message history for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>All messages in chronological order.</returns>
    /// <response code="200">Returns the conversation history.</response>
    /// <response code="403">If the caller's role does not grant conversation:read, whatever the id.</response>
    /// <response code="404">If the conversation does not exist or is not the caller's.</response>
    [HttpGet("{conversationId:guid}/history")]
    [Authorize(Policy = "CanReadConversation")]
    [ProducesResponseType(typeof(ConversationHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationHistoryDto>> GetHistory(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var history = await _chatService.GetHistoryAsync(conversationId, _currentUser.UserId, cancellationToken);

        if (history is null)
            return NotFound();

        return Ok(history);
    }
}
