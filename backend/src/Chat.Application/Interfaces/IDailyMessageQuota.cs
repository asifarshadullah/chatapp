namespace Chat.Application.Interfaces;

/// <summary>
/// Counts each user's messages per UTC day, so one user cannot use up a model allowance that
/// every user shares.
/// </summary>
public interface IDailyMessageQuota
{
    /// <summary>
    /// Counts one message for today and says whether it is within the limit. The limit is passed
    /// in, so a user whose plan changes mid-day is measured against the new one.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid userId, int limit, CancellationToken ct = default);
}
