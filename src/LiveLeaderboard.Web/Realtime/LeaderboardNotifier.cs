using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Leaderboards;
using Microsoft.AspNetCore.SignalR;

namespace LiveLeaderboard.Web.Realtime;

public sealed class LeaderboardNotifier(IHubContext<LeaderboardHub> hubContext) : IProjectionNotifier
{
    public Task PublishAsync(LeaderboardSnapshot snapshot, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(LeaderboardGroupNames.ForCompetition(snapshot.CompetitionId))
            .SendAsync("leaderboardUpdated", snapshot, cancellationToken);
}
