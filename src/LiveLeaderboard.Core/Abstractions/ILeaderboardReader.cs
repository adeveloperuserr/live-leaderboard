using LiveLeaderboard.Core.Leaderboards;

namespace LiveLeaderboard.Core.Abstractions;

public interface ILeaderboardReader
{
    Task<LeaderboardReadResult> GetSnapshotAsync(
        Guid competitionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed record LeaderboardReadResult(
    LeaderboardSnapshot? Snapshot,
    bool IsAvailable,
    string? ErrorMessage)
{
    public static LeaderboardReadResult Available(LeaderboardSnapshot snapshot) => new(snapshot, true, null);

    public static LeaderboardReadResult Unavailable(string message) => new(null, false, message);
}
