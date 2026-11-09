using Chat.Application.Interfaces;
using Chat.Billing.Application.Interfaces;

namespace Chat.Api.Billing;

/// <summary>
/// Gives the chat the daily limit of the user's plan. It lives in the composition root, so the
/// Chat context never references Billing.
/// </summary>
public class PlanDailyMessageAllowance : IDailyMessageAllowance
{
    private readonly IPlanFeatureService _plans;

    public PlanDailyMessageAllowance(IPlanFeatureService plans)
    {
        _plans = plans;
    }

    public async Task<DailyAllowance> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var allowance = await _plans.GetMessageAllowanceAsync(userId, ct);
        return new DailyAllowance(allowance.Limit, allowance.UpgradeLimit);
    }
}
