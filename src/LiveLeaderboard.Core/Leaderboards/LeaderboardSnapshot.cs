namespace LiveLeaderboard.Core.Leaderboards;

public sealed record LeaderboardSnapshot(
    IReadOnlyList<LeaderboardRow> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore,
    DateTime ProjectedAtUtc)
{
    public Guid CompetitionId { get; init; }
}

public static class LeaderboardSnapshotMapper
{
    public static LeaderboardSnapshot Map(
        IEnumerable<LeaderboardSourceRow> sourceRows,
        int page,
        int pageSize,
        int totalCount,
        LeaderboardSnapshot? previous = null)
    {
        var ordered = sourceRows
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ParticipantId)
            .ToArray();

        var previousRanks = previous?.Rows.ToDictionary(row => row.ParticipantId, row => row.Rank);
        var rows = new List<LeaderboardRow>(ordered.Length);
        var lastScore = int.MaxValue;
        var rank = 0;

        for (var index = 0; index < ordered.Length; index++)
        {
            var source = ordered[index];
            if (index == 0 || source.Score != lastScore)
            {
                rank = index + 1;
                lastScore = source.Score;
            }

            int? rankDelta = null;
            if (previousRanks is not null && previousRanks.TryGetValue(source.ParticipantId, out var previousRank))
            {
                rankDelta = previousRank - rank;
            }

            rows.Add(new LeaderboardRow(
                source.ParticipantId,
                source.DisplayName,
                source.Score,
                rank,
                source.SortOrder,
                source.Handle,
                rankDelta));
        }

        return new LeaderboardSnapshot(
            rows,
            page,
            pageSize,
            totalCount,
            page * pageSize < totalCount,
            DateTime.UtcNow);
    }
}
