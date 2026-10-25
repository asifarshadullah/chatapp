using System.Collections.Concurrent;
using Chat.Application.Interfaces;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Counts messages per user in memory. It allows everything unless a test sets a limit, so tests
/// that are not about the limit cannot run into it.
/// </summary>
public class InMemoryDailyMessageQuota : IDailyMessageQuota
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public int Limit { get; set; } = int.MaxValue;

    public int CountFor(Guid userId) => _counts.GetValueOrDefault(userId);

    public Task<bool> TryConsumeAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_counts.AddOrUpdate(userId, 1, (_, count) => count + 1) <= Limit);
}
