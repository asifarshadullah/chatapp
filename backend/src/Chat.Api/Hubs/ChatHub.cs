using Chat.Application.Interfaces;
using Chat.Billing.Application.Interfaces;
using Chat.Identity.Application.Interfaces;
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
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Initializes ChatHub with the application chat service, plan feature service, and current user.
    /// </summary>
    public ChatHub(IChatService chatService, IPlanFeatureService planFeatureService, ICurrentUser currentUser)
    {
        _chatService = chatService;
        _planFeatureService = planFeatureService;
        _currentUser = currentUser;
    }

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

        var chatEnabled = await _planFeatureService.IsEnabledAsync("chat", _currentUser.UserId, cancellationToken);
        if (!chatEnabled)
            throw new HubException("Chat is not available on your current plan.");

        bool conversationIdSent = false;

        await foreach (var (convId, token) in _chatService.StreamResponseAsync(message, conversationId, cancellationToken))
        {
            if (!conversationIdSent)
            {
                await Clients.Caller.SendAsync("ReceiveConversationId", convId, cancellationToken);
                conversationIdSent = true;
            }
            yield return token;
        }
    }
}
