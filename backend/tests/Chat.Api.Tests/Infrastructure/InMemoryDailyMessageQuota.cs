using System.Collections.Concurrent;
using Chat.Application.Interfaces;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>Counts messages per user in memory, against whatever limit each message is given.</summary>
public class InMemoryDailyMessageQuota : IDailyMessageQuota
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public int CountFor(Guid userId) => _counts.GetValueOrDefault(userId);

    public Task<bool> TryConsumeAsync(Guid userId, int limit, CancellationToken ct = default) =>
        Task.FromResult(_counts.AddOrUpdate(userId, 1, (_, count) => count + 1) <= limit);
}
