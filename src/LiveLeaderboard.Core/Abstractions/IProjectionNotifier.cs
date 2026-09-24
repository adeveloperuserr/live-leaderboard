using LiveLeaderboard.Core.Leaderboards;

namespace LiveLeaderboard.Core.Abstractions;

public interface IProjectionNotifier
{
    Task PublishAsync(LeaderboardSnapshot snapshot, CancellationToken cancellationToken = default);
}
