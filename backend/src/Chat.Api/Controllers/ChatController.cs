using Chat.Api.Contracts;
using Chat.Api.Streaming;
using Chat.Application.DTOs;
using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Billing.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Api.Controllers;

/// <summary>
/// Accepts chat messages, streaming each reply back, and serves conversation history.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ICurrentUser _currentUser;
    private readonly IPlanFeatureService _planFeatureService;
    private readonly ILogger<ChatController> _logger;

    private const string ReplyFailedMessage = "The reply could not be completed.";
    private const string ModelRateLimitedMessage = "The demo has reached its model limit for now. Please try again later.";

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatController"/> class.
    /// </summary>
    /// <param name="chatService">The chat service for processing messages.</param>
    /// <param name="currentUser">The signed-in caller, whose conversations this controller serves.</param>
    /// <param name="planFeatureService">Decides whether the caller's plan includes chat.</param>
    /// <param name="logger">Records why a reply failed, which the caller is not told.</param>
    public ChatController(
        IChatService chatService,
        ICurrentUser currentUser,
        IPlanFeatureService planFeatureService,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _currentUser = currentUser;
        _planFeatureService = planFeatureService;
        _logger = logger;
    }

    /// <summary>
    /// Streams the AI reply as server-sent events: <c>conversation</c>, then <c>token</c>s, then <c>done</c>.
    /// </summary>
    /// <param name="request">The message, and the conversation to continue or null for a new one.</param>
    /// <param name="cancellationToken">Signals that the caller has gone away, which stops the reply.</param>
    /// <response code="200">The reply, as an event stream.</response>
    /// <response code="400">If the message is empty or too long, whatever conversation it names.</response>
    /// <response code="403">If the caller's role does not grant conversation:create, or their plan does not include chat.</response>
    /// <response code="404">If the conversation does not exist or is not the caller's.</response>
    /// <response code="429">If the caller has already sent today's allowed number of messages.</response>
    /// <response code="502">If the model fails before its first token.</response>
    /// <response code="503">If the model service is at its rate limit before the first token.</response>
    [HttpPost("messages")]
    [Authorize(Policy = "CanChat")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        // Content first, so the answer reveals nothing about the conversation named.
        if (string.IsNullOrWhiteSpace(request.Message))
            return Problem(detail: "Message cannot be empty.", statusCode: StatusCodes.Status400BadRequest);
        if (request.Message.Length > ChatMessage.MaxContentLength)
            return Problem(
                detail: $"Message cannot be longer than {ChatMessage.MaxContentLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        var userId = _currentUser.UserId;
        if (!await _planFeatureService.IsEnabledAsync("chat", userId, cancellationToken))
            return Problem(detail: "Chat is not available on your current plan.", statusCode: StatusCodes.Status403Forbidden);

        await using var items = _chatService
            .StreamResponseAsync(request.Message, userId, request.ConversationId, ct: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        // The first step looks up the conversation, so refusals still precede any output.
        bool hasFirst;
        try
        {
            hasFirst = await items.MoveNextAsync();
        }
        catch (ConversationNotFoundException)
        {
            // Same answer whether the conversation is missing or someone else's.
            return Problem(detail: "Conversation not found.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (DailyMessageLimitReachedException ex)
        {
            return Problem(
                detail: $"You have reached today's limit of {ex.Limit} messages. It resets at midnight UTC.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (ModelRateLimitedException ex)
        {
            _logger.LogWarning(ex, "The model service refused the reply at its rate limit");
            return Problem(detail: ModelRateLimitedMessage, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The reply failed before its first token");
            return Problem(detail: ReplyFailedMessage, statusCode: StatusCodes.Status502BadGateway);
        }

        var stream = EventStreamWriter.Start(Response);
        try
        {
            if (hasFirst)
            {
                await stream.WriteAsync("conversation", items.Current.ConversationId, cancellationToken);
                do
                {
                    await stream.WriteAsync("token", items.Current.Token, cancellationToken);
                }
                while (await items.MoveNextAsync());
            }
            await stream.WriteAsync("done", new { }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The reply failed part-way through");
            await stream.WriteAsync("error", new { message = ReplyFailedMessage }, cancellationToken);
        }
        return new EmptyResult();
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
