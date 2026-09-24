using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Redis;

public sealed class RedisLeaderboardReader(ApplicationDbContext db, RedisLeaderboardStore store) : ILeaderboardReader
{
    public async Task<LeaderboardReadResult> GetSnapshotAsync(
        Guid competitionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (!await store.ExistsAsync(competitionId, cancellationToken))
        {
            return LeaderboardReadResult.Unavailable("The Redis leaderboard projection is not available yet.");
        }

        var totalCount = checked((int)await store.CountAsync(competitionId, cancellationToken));
        var end = Math.Min(totalCount - 1, (long)page * pageSize - 1);
        var entries = totalCount == 0
            ? Array.Empty<RedisScoreEntry>()
            : await store.ReadOrderedAsync(competitionId, 0, end, cancellationToken);

        var participantIds = entries
            .Select(entry => Guid.ParseExact(entry.Member, "N"))
            .ToArray();
        var participants = await db.Participants
            .AsNoTracking()
            .Where(participant => participant.CompetitionId == competitionId && participant.IsActive && participantIds.Contains(participant.Id))
            .ToDictionaryAsync(participant => participant.Id, cancellationToken);

        var sourceRows = entries
            .Select(entry =>
            {
                var participantId = Guid.ParseExact(entry.Member, "N");
                return participants.TryGetValue(participantId, out var participant)
                    ? new LeaderboardSourceRow(participant.Id, participant.DisplayName, checked((int)entry.Score), participant.SortOrder, participant.Handle)
                    : null;
            })
            .Where(row => row is not null)
            .Cast<LeaderboardSourceRow>();

        var completeSnapshot = LeaderboardSnapshotMapper.Map(sourceRows, page, pageSize, totalCount);
        var pageRows = completeSnapshot.Rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return LeaderboardReadResult.Available(completeSnapshot with { CompetitionId = competitionId, Rows = pageRows });
    }
}
