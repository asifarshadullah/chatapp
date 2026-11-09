using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Exceptions;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Configuration;

using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Chat.Api.Controllers;

/// <summary>Billing endpoints: plans, subscriptions, and Stripe webhook.</summary>
[ApiController]
[Route("billing")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IPlanRepository _planRepository;
    private readonly ICurrentUser _currentUser;
    private readonly StripeSettings _stripeSettings;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        ISubscriptionService subscriptionService,
        IPlanRepository planRepository,
        ICurrentUser currentUser,
        IOptions<StripeSettings> stripeSettings,
        ILogger<BillingController> logger)
    {
        _subscriptionService = subscriptionService;
        _planRepository = planRepository;
        _currentUser = currentUser;
        _stripeSettings = stripeSettings.Value;
        _logger = logger;
    }

    /// <summary>Returns all available subscription plans.</summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IEnumerable<PlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PlanDto>>> GetPlans(CancellationToken ct)
    {
        var plans = await _planRepository.GetAllAsync(ct);
        var dtos = plans.OrderBy(p => p.Tier).Select(p => new PlanDto(
            p.Id,
            p.Name,
            p.Tier.ToString(),
            p.PricePerMonth,
            p.DailyMessageLimit,
            p.Features.Select(f => f.ToString()).ToList().AsReadOnly()));
        return Ok(dtos);
    }

    /// <summary>Says whether users can upgrade here, and whether Stripe is in test mode.</summary>
    [HttpGet("checkout-mode")]
    [ProducesResponseType(typeof(CheckoutModeResponse), StatusCodes.Status200OK)]
    public ActionResult<CheckoutModeResponse> GetCheckoutMode() =>
        Ok(new CheckoutModeResponse(_stripeSettings.Mode.ToString().ToLowerInvariant()));

    /// <summary>Creates a Stripe Checkout session for the requested plan.</summary>
    [HttpPost("subscribe")]
    [ProducesResponseType(typeof(CheckoutSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CheckoutSessionDto>> Subscribe(
        [FromBody] SubscribeRequest request, CancellationToken ct)
    {
        // Checked first, so a demo without Stripe never reaches it or the plan store.
        if (_stripeSettings.Mode == CheckoutMode.Off)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Upgrades are turned off on this demo. No payment was taken.");

        var plan = await _planRepository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return NotFound("Plan not found.");
        if (plan.Tier == PlanTier.Free || !_stripeSettings.PriceIds.TryGetValue(plan.Tier.ToString(), out var stripePriceId))
            return BadRequest($"{plan.Name} needs no checkout.");

        var successUrl = $"{Request.Scheme}://{Request.Host}/billing/success";
        var cancelUrl = $"{Request.Scheme}://{Request.Host}/billing/cancel";

        try
        {
            return Ok(await _subscriptionService.SubscribeAsync(
                _currentUser.UserId, request.PlanId, stripePriceId, successUrl, cancelUrl, ct));
        }
        catch (AlreadySubscribedException ex)
        {
            return Conflict(ex.Message);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogError(ex, "Stripe refused to start a checkout");
            return StatusCode(StatusCodes.Status502BadGateway, "The upgrade could not be started. Please try again later.");
        }
    }

    /// <summary>Returns the user's current plan, which is Free when they have no paid one.</summary>
    [HttpGet("subscription")]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubscriptionStatusDto>> GetSubscription(CancellationToken ct) =>
        Ok(await _subscriptionService.GetSubscriptionStatusAsync(_currentUser.UserId, ct));

    /// <summary>Ends the user's paid plan when its current period does; until then they keep it.</summary>
    [HttpDelete("subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CancelSubscription(CancellationToken ct)
    {
        try
        {
            await _subscriptionService.CancelSubscriptionAsync(_currentUser.UserId, ct);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound("No active subscription found.");
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogError(ex, "Stripe refused to cancel a subscription");
            return StatusCode(StatusCodes.Status502BadGateway, "The subscription could not be cancelled. Please try again later.");
        }
    }
}

/// <summary>Request body for POST /billing/subscribe.</summary>
public record SubscribeRequest(Guid PlanId);

/// <summary>Response body for GET /billing/checkout-mode: "off", "test" or "live".</summary>
public record CheckoutModeResponse(string Mode);
