using Chat.Billing.Application.DTOs;
using Chat.Billing.Application.Interfaces;

namespace Chat.Billing.Infrastructure.Services;

/// <summary>
/// Stub implementation that enables every feature.
/// Replaced in production once real billing plans are implemented.
/// </summary>
public class StubPlanFeatureService : IPlanFeatureService
{
    public Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<MessageAllowance> GetMessageAllowanceAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(new MessageAllowance(int.MaxValue, null));
}
