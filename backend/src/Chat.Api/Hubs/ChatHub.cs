using Chat.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace Chat.Api.Hubs;

/// <summary>
/// SignalR hub that streams chat echo responses word-by-word.
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
    /// Accepts a user message, stores it, then streams the echo response word by word.
    /// Sends "ReceiveConversationId" to the caller before the first word.
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

        var response = await _chatService.SendMessageAsync(message, conversationId, cancellationToken);

        await Clients.Caller.SendAsync("ReceiveConversationId", response.ConversationId, cancellationToken);

        foreach (var word in response.Message.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return word + " ";
            await Task.Delay(50, cancellationToken);
        }
    }
}
