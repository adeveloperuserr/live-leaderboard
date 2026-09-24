namespace LiveLeaderboard.Core.Scoring;

public static class ProjectionRetryPolicy
{
    public const int MaxAttempts = 6;

    public static TimeSpan GetDelay(int attempts)
    {
        var seconds = attempts switch
        {
            <= 1 => 1,
            2 => 2,
            3 => 4,
            4 => 8,
            5 => 16,
            _ => 30
        };
        return TimeSpan.FromSeconds(seconds);
    }

    public static bool ShouldFail(int attempts) => attempts >= MaxAttempts;
}
