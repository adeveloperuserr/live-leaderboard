namespace LiveLeaderboard.Data.Redis;

public static class RedisKeyFactory
{
    public static string Leaderboard(Guid competitionId) => $"leaderboard:{competitionId:N}";

    public static string Rebuild(Guid competitionId, Guid operationId) =>
        $"leaderboard:{competitionId:N}:rebuild:{operationId:N}";
}
