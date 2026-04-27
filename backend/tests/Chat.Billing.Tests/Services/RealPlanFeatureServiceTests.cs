using Chat.Billing.Application.Interfaces;
using Chat.Billing.Domain.Entities;
using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Services;
using FluentAssertions;

namespace Chat.Billing.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

public class FakePlanRepository : IPlanRepository
{
    private readonly List<Plan> _plans = [];

    public FakePlanRepository(params Plan[] plans) => _plans.AddRange(plans);

    public Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Plan>>(_plans.AsReadOnly());

    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_plans.FirstOrDefault(p => p.Id == id));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class RealPlanFeatureServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProPlanId = Guid.NewGuid();

    private static readonly Plan ProPlan = new(
        ProPlanId, "Pro", PlanTier.Pro, 19m,
        [Feature.Chat, Feature.DocumentUpload]);

    private static Subscription ActiveProSub() =>
        new(Guid.NewGuid(), UserId, ProPlanId, SubscriptionStatus.Active,
            DateTime.UtcNow.AddDays(30), "sub_pro_123");

    private static RealPlanFeatureService Build(Subscription? sub, Plan? plan)
    {
        var repo = new FakeSubscriptionRepository();
        if (sub is not null) repo.SaveAsync(sub).GetAwaiter().GetResult();
        var plans = new FakePlanRepository(plan is not null ? [plan] : []);
        return new RealPlanFeatureService(repo, plans);
    }

    // ── Cycle 3.1 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsFeatureEnabled_ActiveProSubscription_Chat_ReturnsTrue()
    {
        var sut = Build(ActiveProSub(), ProPlan);
        var result = await sut.IsEnabledAsync("chat", UserId);
        result.Should().BeTrue();
    }

    // ── Cycle 3.2 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsFeatureEnabled_ActiveProSubscription_DocumentUpload_ReturnsTrue()
    {
        var sut = Build(ActiveProSub(), ProPlan);
        var result = await sut.IsEnabledAsync("documentupload", UserId);
        result.Should().BeTrue();
    }

    // ── Cycle 3.3 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsFeatureEnabled_NoSubscription_Chat_ReturnsTrue()
    {
        var sut = Build(null, null);
        var result = await sut.IsEnabledAsync("chat", UserId);
        result.Should().BeTrue();
    }

    // ── Cycle 3.4 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsFeatureEnabled_NoSubscription_DocumentUpload_ReturnsFalse()
    {
        var sut = Build(null, null);
        var result = await sut.IsEnabledAsync("documentupload", UserId);
        result.Should().BeFalse();
    }

    // ── Cycle 3.5 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsFeatureEnabled_CancelledSubscription_DocumentUpload_ReturnsFalse()
    {
        var cancelled = new Subscription(Guid.NewGuid(), UserId, ProPlanId, SubscriptionStatus.Cancelled,
            DateTime.UtcNow.AddDays(-1), "sub_cancelled");
        var sut = Build(cancelled, ProPlan);
        var result = await sut.IsEnabledAsync("documentupload", UserId);
        result.Should().BeFalse();
    }
}
