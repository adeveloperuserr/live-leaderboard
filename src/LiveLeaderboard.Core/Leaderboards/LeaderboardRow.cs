namespace LiveLeaderboard.Core.Leaderboards;

public sealed record LeaderboardSourceRow(
    Guid ParticipantId,
    string DisplayName,
    int Score,
    int SortOrder,
    string? Handle = null);

public sealed record LeaderboardRow(
    Guid ParticipantId,
    string DisplayName,
    int Score,
    int Rank,
    int SortOrder,
    string? Handle,
    int? RankDelta);
