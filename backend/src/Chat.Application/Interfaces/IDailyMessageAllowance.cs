namespace Chat.Application.Interfaces;

/// <summary>How many messages a user may send today, which their plan decides.</summary>
public interface IDailyMessageAllowance
{
    Task<DailyAllowance> GetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// A user's daily limit, and the limit upgrading would give them, or null when there is nothing
/// to upgrade to.
/// </summary>
public record DailyAllowance(int Limit, int? UpgradeLimit);
