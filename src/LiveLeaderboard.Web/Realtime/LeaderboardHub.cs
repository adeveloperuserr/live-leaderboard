using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Data.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Web.Realtime;

public sealed class LeaderboardHub(ApplicationDbContext db) : Hub
{
    public static bool IsPublic(CompetitionStatus status) =>
        status is CompetitionStatus.Live or CompetitionStatus.Completed;

    public async Task<bool> JoinCompetition(Guid competitionId)
    {
        var status = await db.Competitions
            .AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => (CompetitionStatus?)competition.Status)
            .SingleOrDefaultAsync();

        if (status is null || !IsPublic(status.Value))
        {
            return false;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LeaderboardGroupNames.ForCompetition(competitionId));
        return true;
    }
}
