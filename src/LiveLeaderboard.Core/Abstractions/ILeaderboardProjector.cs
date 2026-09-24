using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Core.Scoring;

namespace LiveLeaderboard.Core.Abstractions;

public interface ILeaderboardProjector
{
    Task<LeaderboardSnapshot> ProjectAsync(
        ProjectionOutbox work,
        CancellationToken cancellationToken = default);
}
