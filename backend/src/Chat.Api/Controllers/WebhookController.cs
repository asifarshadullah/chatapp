using Chat.Billing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace Chat.Api.Controllers;

/// <summary>Receives Stripe webhook events. Stripe signature validates authenticity — no JWT required.</summary>
[ApiController]
[AllowAnonymous]
public class WebhookController : ControllerBase
{
    private readonly IWebhookHandler _webhookHandler;

    public WebhookController(IWebhookHandler webhookHandler)
    {
        _webhookHandler = webhookHandler;
    }

    /// <summary>Stripe webhook endpoint. Validates Stripe-Signature header before processing.</summary>
    [HttpPost("/webhooks/stripe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken ct)
    {
        var payload = await new StreamReader(Request.Body).ReadToEndAsync(ct);
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;

        try
        {
            await _webhookHandler.HandleAsync(payload, signature, ct);
            return Ok();
        }
        catch (StripeException)
        {
            return BadRequest("Invalid Stripe signature.");
        }
    }
}
