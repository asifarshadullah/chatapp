using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;
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

    public BillingController(
        ISubscriptionService subscriptionService,
        IPlanRepository planRepository,
        ICurrentUser currentUser,
        IOptions<StripeSettings> stripeSettings)
    {
        _subscriptionService = subscriptionService;
        _planRepository = planRepository;
        _currentUser = currentUser;
        _stripeSettings = stripeSettings.Value;
    }

    /// <summary>Returns all available subscription plans.</summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IEnumerable<PlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PlanDto>>> GetPlans(CancellationToken ct)
    {
        var plans = await _planRepository.GetAllAsync(ct);
        var dtos = plans.Select(p => new PlanDto(
            p.Id,
            p.Name,
            p.Tier.ToString(),
            p.PricePerMonth,
            p.Features.Select(f => f.ToString()).ToList().AsReadOnly()));
        return Ok(dtos);
    }

    /// <summary>Creates a Stripe Checkout session for the requested plan.</summary>
    [HttpPost("subscribe")]
    [ProducesResponseType(typeof(CheckoutSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CheckoutSessionDto>> Subscribe(
        [FromBody] SubscribeRequest request, CancellationToken ct)
    {
        var plan = await _planRepository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return NotFound("Plan not found.");

        var tierKey = plan.Tier.ToString();
        if (!_stripeSettings.PriceIds.TryGetValue(tierKey, out var stripePriceId))
            return BadRequest($"No Stripe price configured for tier '{tierKey}'.");

        var successUrl = $"{Request.Scheme}://{Request.Host}/billing/success";
        var cancelUrl = $"{Request.Scheme}://{Request.Host}/billing/cancel";

        var session = await _subscriptionService.SubscribeAsync(
            _currentUser.UserId, request.PlanId, stripePriceId, successUrl, cancelUrl, ct);

        return Ok(session);
    }

    /// <summary>Returns the current subscription status, or 404 if the user has no subscription.</summary>
    [HttpGet("subscription")]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubscriptionStatusDto>> GetSubscription(CancellationToken ct)
    {
        var status = await _subscriptionService.GetSubscriptionStatusAsync(_currentUser.UserId, ct);
        if (status is null)
            return NotFound();
        return Ok(status);
    }

    /// <summary>Cancels the current subscription.</summary>
    [HttpDelete("subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    }
}

/// <summary>Request body for POST /billing/subscribe.</summary>
public record SubscribeRequest(Guid PlanId);
