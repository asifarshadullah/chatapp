using Chat.Application.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Chat.Infrastructure.Quota;

/// <summary>
/// One counter document per user and UTC day, incremented in a single atomic step, so parallel
/// messages cannot both take the last place and a restart does not reset anyone's count.
/// </summary>
public class MongoDailyMessageQuota : IDailyMessageQuota
{
    public const string CollectionName = "daily_message_counts";

    private readonly IMongoCollection<BsonDocument> _counters;
    private readonly TimeProvider _clock;

    public MongoDailyMessageQuota(IMongoDatabase database, int limit, TimeProvider clock)
    {
        _counters = database.GetCollection<BsonDocument>(CollectionName);
        Limit = limit;
        _clock = clock;
    }

    public int Limit { get; }

    /// <inheritdoc/>
    public async Task<bool> TryConsumeAsync(Guid userId, CancellationToken ct = default)
    {
        var today = _clock.GetUtcNow().UtcDateTime.Date;
        var day = today.ToString("yyyy-MM-dd");

        // The server retries an upsert that loses a race to insert the same _id, so the first
        // messages of a day cannot fail each other.
        var counter = await _counters.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", $"{userId}:{day}"),
            Builders<BsonDocument>.Update
                .Inc("count", 1)
                .SetOnInsert("userId", userId.ToString())
                .SetOnInsert("day", day)
                .SetOnInsert("expiresAt", today.AddDays(2)),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);

        return counter["count"].AsInt32 <= Limit;
    }

    /// <summary>Lets MongoDB delete each day's counters once that day is long over.</summary>
    public static Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct = default) =>
        database.GetCollection<BsonDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);
}
