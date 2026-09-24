namespace LiveLeaderboard.Web.Realtime;

public static class LeaderboardGroupNames
{
    public static string ForCompetition(Guid competitionId) => $"competition:{competitionId:N}";
}
