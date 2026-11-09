using Chat.Application.Interfaces;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Gives every user the same allowance, unlimited unless a test sets one, so tests that are not
/// about the limit cannot run into it.
/// </summary>
public class FakeDailyMessageAllowance : IDailyMessageAllowance
{
    public DailyAllowance Allowance { get; set; } = new(int.MaxValue, null);

    public Task<DailyAllowance> GetAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Allowance);
}
