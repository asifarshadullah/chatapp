using Chat.Application.Exceptions;
using Chat.Application.Interfaces;
using Chat.Billing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace Chat.Api.Hubs;

/// <summary>
/// SignalR hub that streams AI responses token by token.
/// </summary>
[Authorize(Policy = "CanChat")]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IPlanFeatureService _planFeatureService;

    /// <summary>
    /// Initializes ChatHub with the application chat service and plan feature service.
    /// </summary>
    public ChatHub(IChatService chatService, IPlanFeatureService planFeatureService)
    {
        _chatService = chatService;
        _planFeatureService = planFeatureService;
    }

    /// <summary>
    /// The caller, read from the authenticated connection rather than the current HTTP request.
    /// Under long polling the request that delivered the message has already ended when a hub
    /// method runs, so only the connection still knows who is calling. Empty when the
    /// connection carries no valid 'sub' claim.
    /// </summary>
    private Guid CallerId =>
        Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

    /// <summary>
    /// Accepts a user message, saves it, then streams AI response tokens one at a time.
    /// Sends "ReceiveConversationId" to the caller before the first token.
    /// Throws HubException if the user's plan does not include the chat feature.
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <param name="conversationId">Optional existing conversation to continue. Null creates a new one.</param>
    /// <param name="cancellationToken">Injected by SignalR; signals client disconnection.</param>
    public async IAsyncEnumerable<string> SendMessage(
        string message,
        Guid? conversationId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new HubException("Message cannot be empty.");

        var userId = CallerId;

        var chatEnabled = await _planFeatureService.IsEnabledAsync("chat", userId, cancellationToken);
        if (!chatEnabled)
            throw new HubException("Chat is not available on your current plan.");

        bool conversationIdSent = false;

        // Stepped by hand because C# forbids yield inside a try that has a catch. The
        // conversation is looked up before the first item, so a refusal surfaces on the first
        // MoveNextAsync, before any conversation id or token reaches the caller.
        await using var items = _chatService
            .StreamResponseAsync(message, userId, conversationId, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            try
            {
                if (!await items.MoveNextAsync())
                    break;
            }
            catch (ConversationNotFoundException)
            {
                // Same answer whether the conversation is missing or someone else's.
                throw new HubException("Conversation not found.");
            }

            var (convId, token) = items.Current;
            if (!conversationIdSent)
            {
                await Clients.Caller.SendAsync("ReceiveConversationId", convId, cancellationToken);
                conversationIdSent = true;
            }
            yield return token;
        }
    }
}
