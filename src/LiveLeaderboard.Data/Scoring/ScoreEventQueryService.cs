using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Scoring;

public sealed record RecentScoreEvent(
    Guid Id,
    Guid ParticipantId,
    int Points,
    int TotalAfterScore,
    string Reason,
    DateTime CreatedAtUtc);

public sealed class ScoreEventQueryService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<RecentScoreEvent>> GetRecentAsync(
        Guid competitionId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        return await db.ScoreEvents
            .AsNoTracking()
            .Where(scoreEvent => scoreEvent.CompetitionId == competitionId)
            .OrderByDescending(scoreEvent => scoreEvent.CreatedAtUtc)
            .Take(limit)
            .Select(scoreEvent => new RecentScoreEvent(
                scoreEvent.Id,
                scoreEvent.ParticipantId,
                scoreEvent.Points,
                scoreEvent.TotalAfterScore,
                scoreEvent.Reason,
                scoreEvent.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
