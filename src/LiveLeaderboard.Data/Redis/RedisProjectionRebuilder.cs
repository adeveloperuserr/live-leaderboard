using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Redis;

public sealed class RedisProjectionRebuilder(ApplicationDbContext db, RedisLeaderboardStore store) : ILeaderboardProjector
{
    public async Task<LeaderboardSnapshot> ProjectAsync(
        ProjectionOutbox work,
        CancellationToken cancellationToken = default)
    {
        await store.SetAbsoluteScoreAsync(work.CompetitionId, work.ParticipantId, work.TotalScore, cancellationToken);
        var reader = new RedisLeaderboardReader(db, store);
        var result = await reader.GetSnapshotAsync(work.CompetitionId, 1, 50, cancellationToken);
        return result.Snapshot ?? throw new InvalidOperationException(result.ErrorMessage ?? "Unable to read the projected leaderboard.");
    }

    public async Task RebuildAsync(Guid competitionId, CancellationToken cancellationToken = default)
    {
        var participants = await db.Participants
            .AsNoTracking()
            .Where(participant => participant.CompetitionId == competitionId && participant.IsActive)
            .Select(participant => new { participant.Id })
            .ToListAsync(cancellationToken);
        var participantIds = participants.Select(participant => participant.Id).ToArray();
        var totals = await db.ParticipantTotals
            .AsNoTracking()
            .Where(total => total.CompetitionId == competitionId && participantIds.Contains(total.ParticipantId))
            .ToDictionaryAsync(total => total.ParticipantId, total => total.TotalScore, cancellationToken);

        var projection = participants.ToDictionary(
            participant => participant.Id,
            participant => totals.GetValueOrDefault(participant.Id));
        await store.ReplaceAsync(competitionId, projection, cancellationToken);
    }
}
