using Chat.Billing.Domain.Enums;
using Chat.Billing.Infrastructure.Configuration;
using Chat.Billing.Infrastructure.Data;
using Chat.Billing.Infrastructure.Repositories;
using Chat.Billing.Tests.Infrastructure;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Billing.Tests.Repositories;

public class PlanSeederTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;

    public PlanSeederTests(MongoDbFixture fixture) => _fixture = fixture;

    private static async Task<List<PlanDocument>> Plans(IMongoDatabase db) =>
        await db.GetCollection<PlanDocument>("plans").Find(FilterDefinition<PlanDocument>.Empty).SortBy(p => p.Tier).ToListAsync();

    [Fact]
    public async Task AnEmptyDatabase_GetsFreeAndPro()
    {
        var db = _fixture.NewDatabase();

        await PlanSeeder.SeedAsync(db, new BillingSettings());

        var plans = await Plans(db);
        plans.Select(p => (p.Tier, p.Name, p.PricePerMonth, p.DailyMessageLimit)).Should().Equal(
            (PlanTier.Free, "Free", 0m, 10),
            (PlanTier.Pro, "Pro", 19m, 100));
        plans.Should().OnlyContain(p => p.Features.SequenceEqual(new[] { Feature.Chat }));
    }

    [Fact]
    public async Task AChangedLimit_IsWritten_AndThePlanKeepsItsId()
    {
        var db = _fixture.NewDatabase();
        await PlanSeeder.SeedAsync(db, new BillingSettings());
        var proId = (await Plans(db)).Single(p => p.Tier == PlanTier.Pro).Id;
        var changed = new BillingSettings();
        changed.Plans.Pro.DailyMessages = 250;

        await PlanSeeder.SeedAsync(db, changed);

        var pro = (await Plans(db)).Single(p => p.Tier == PlanTier.Pro);
        pro.Id.Should().Be(proId);
        pro.DailyMessageLimit.Should().Be(250);
    }

    [Fact]
    public async Task SeedingTwice_LeavesTheSameTwoPlans()
    {
        var db = _fixture.NewDatabase();
        await PlanSeeder.SeedAsync(db, new BillingSettings());
        var first = (await Plans(db)).Select(p => p.Id).ToList();

        await PlanSeeder.SeedAsync(db, new BillingSettings());

        (await Plans(db)).Select(p => p.Id).Should().Equal(first);
    }

    [Fact]
    public async Task APlanOfAnotherTier_IsRemoved()
    {
        var db = _fixture.NewDatabase();
        await db.GetCollection<PlanDocument>("plans").InsertOneAsync(new PlanDocument
        {
            Id = Guid.NewGuid(), Name = "Enterprise", Tier = PlanTier.Enterprise, PricePerMonth = 99m,
        });

        await PlanSeeder.SeedAsync(db, new BillingSettings());

        (await Plans(db)).Select(p => p.Tier).Should().Equal(PlanTier.Free, PlanTier.Pro);
    }

    [Fact]
    public async Task APlanStoredWithABinaryId_IsReplaced_SoItCanBeRead()
    {
        var db = _fixture.NewDatabase();
        await db.GetCollection<BsonDocument>("plans").InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.CSharpLegacy) },
            { "Name", "Pro" }, { "Tier", (int)PlanTier.Pro }, { "PricePerMonth", "19" }, { "Features", new BsonArray() },
        });

        await PlanSeeder.SeedAsync(db, new BillingSettings());

        (await new MongoPlanRepository(db).GetAllAsync()).Select(p => p.Tier).Should().BeEquivalentTo([PlanTier.Free, PlanTier.Pro]);
    }
}
