using Chat.Billing.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// ChatApiFactory variant that replaces IPlanFeatureService with a stub that always returns false,
/// simulating a plan that has the chat feature disabled.
/// </summary>
public class DisabledChatFeatureFactory : ChatApiFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPlanFeatureService));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IPlanFeatureService, AlwaysDisablePlanFeatureService>();
        });
    }
}

/// <summary>Stub that disables every feature — simulates a locked-down plan.</summary>
public class AlwaysDisablePlanFeatureService : IPlanFeatureService
{
    public Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default)
        => Task.FromResult(false);
}

/// <summary>
/// ChatApiFactory variant whose plan service allows chat and records which user each check was for.
/// </summary>
public class RecordingPlanFeatureFactory : ChatApiFactory
{
    public RecordingPlanFeatureService PlanService { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPlanFeatureService));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IPlanFeatureService>(PlanService);
        });
    }
}

/// <summary>Stub that enables every feature and records the user id of each check.</summary>
public class RecordingPlanFeatureService : IPlanFeatureService
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Guid> _checked = new();

    public IReadOnlyCollection<Guid> CheckedUserIds => _checked.ToArray();

    public Task<bool> IsEnabledAsync(string feature, Guid userId, CancellationToken ct = default)
    {
        _checked.Enqueue(userId);
        return Task.FromResult(true);
    }
}
