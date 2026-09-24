using StackExchange.Redis;

namespace LiveLeaderboard.Data.Redis;

public sealed record RedisScoreEntry(string Member, double Score);

public sealed class RedisLeaderboardStore(IConnectionMultiplexer connection)
{
    private readonly IDatabase database = connection.GetDatabase();

    public Task SetAbsoluteScoreAsync(
        Guid competitionId,
        Guid participantId,
        int totalScore,
        CancellationToken cancellationToken = default) =>
        database.SortedSetAddAsync(
            RedisKeyFactory.Leaderboard(competitionId),
            participantId.ToString("N"),
            totalScore);

    public async Task<IReadOnlyList<RedisScoreEntry>> ReadOrderedAsync(
        Guid competitionId,
        long start,
        long stop,
        CancellationToken cancellationToken = default)
    {
        var entries = await database.SortedSetRangeByRankWithScoresAsync(
            RedisKeyFactory.Leaderboard(competitionId),
            start,
            stop,
            Order.Descending);

        return entries
            .Select(entry => new RedisScoreEntry(entry.Element.ToString(), entry.Score))
            .ToArray();
    }

    public Task<long> CountAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        database.SortedSetLengthAsync(RedisKeyFactory.Leaderboard(competitionId));

    public Task<bool> ExistsAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        database.KeyExistsAsync(RedisKeyFactory.Leaderboard(competitionId));

    public Task RemoveAsync(Guid competitionId, Guid participantId, CancellationToken cancellationToken = default) =>
        database.SortedSetRemoveAsync(
            RedisKeyFactory.Leaderboard(competitionId),
            participantId.ToString("N"));

    public async Task ReplaceAsync(
        Guid competitionId,
        IReadOnlyDictionary<Guid, int> totals,
        CancellationToken cancellationToken = default)
    {
        var canonicalKey = RedisKeyFactory.Leaderboard(competitionId);
        if (totals.Count == 0)
        {
            await database.KeyDeleteAsync(canonicalKey);
            return;
        }

        var temporaryKey = RedisKeyFactory.Rebuild(competitionId, Guid.NewGuid());
        var entries = totals
            .Select(pair => new SortedSetEntry(pair.Key.ToString("N"), pair.Value))
            .ToArray();

        await database.SortedSetAddAsync(temporaryKey, entries);
        await database.KeyRenameAsync(temporaryKey, canonicalKey, When.Always);
    }
}
