namespace Chat.Billing.Application.Interfaces;

/// <summary>Validates and processes incoming payment provider webhook events.</summary>
public interface IWebhookHandler
{
    /// <summary>Validates the webhook signature and routes the event to the appropriate handler.</summary>
    Task HandleAsync(string payload, string signatureHeader, CancellationToken ct = default);
}
