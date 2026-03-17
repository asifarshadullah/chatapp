using Chat.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace Chat.Api.Hubs;

/// <summary>
/// SignalR hub that streams AI responses token by token.
/// </summary>
public class ChatHub : Hub
{
    private readonly IChatService _chatService;

    /// <summary>
    /// Initializes ChatHub with the application chat service.
    /// </summary>
    public ChatHub(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Accepts a user message, saves it, then streams AI response tokens one at a time.
    /// Sends "ReceiveConversationId" to the caller before the first token.
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
