namespace Chat.Application.Interfaces;

/// <summary>
/// How many messages each user may send per UTC day, so one user cannot use up a model
/// allowance that every user shares.
/// </summary>
public interface IDailyMessageQuota
{
    int Limit { get; }

    /// <summary>Counts one message for today and says whether it is within the limit.</summary>
    Task<bool> TryConsumeAsync(Guid userId, CancellationToken ct = default);
}
