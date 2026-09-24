using FluentAssertions;
using LiveLeaderboard.Core.Leaderboards;

namespace LiveLeaderboard.Tests.Core;

public sealed class LeaderboardSnapshotTests
{
    [Fact]
    public void Map_assigns_competition_ranks_for_equal_scores()
    {
        var rows = new[]
        {
            new LeaderboardSourceRow(Guid.NewGuid(), "Alpha", 100, 2),
            new LeaderboardSourceRow(Guid.NewGuid(), "Bravo", 100, 1),
            new LeaderboardSourceRow(Guid.NewGuid(), "Charlie", 90, 3)
        };

        var snapshot = LeaderboardSnapshotMapper.Map(rows, page: 1, pageSize: 3, totalCount: 3);

        snapshot.Rows.Select(row => row.Rank).Should().Equal(1, 1, 3);
        snapshot.Rows.Select(row => row.DisplayName).Should().Equal("Bravo", "Alpha", "Charlie");
    }

    [Fact]
    public void Map_orders_negative_scores_from_highest_to_lowest()
    {
        var rows = new[]
        {
            new LeaderboardSourceRow(Guid.NewGuid(), "Last", -20, 2),
            new LeaderboardSourceRow(Guid.NewGuid(), "First", -10, 1)
        };

        var snapshot = LeaderboardSnapshotMapper.Map(rows, page: 1, pageSize: 2, totalCount: 2);

        snapshot.Rows.Select(row => row.DisplayName).Should().Equal("First", "Last");
        snapshot.Rows.Select(row => row.Rank).Should().Equal(1, 2);
    }

    [Fact]
    public void Map_reports_more_pages_when_the_sorted_set_has_more_members()
    {
        var rows = new[]
        {
            new LeaderboardSourceRow(Guid.NewGuid(), "One", 10, 1),
            new LeaderboardSourceRow(Guid.NewGuid(), "Two", 9, 2)
        };

        var snapshot = LeaderboardSnapshotMapper.Map(rows, page: 1, pageSize: 2, totalCount: 5);

        snapshot.HasMore.Should().BeTrue();
        snapshot.Page.Should().Be(1);
        snapshot.PageSize.Should().Be(2);
    }

    [Fact]
    public void Map_reports_empty_snapshot_without_inventing_rows()
    {
        var snapshot = LeaderboardSnapshotMapper.Map(
            Array.Empty<LeaderboardSourceRow>(), page: 1, pageSize: 50, totalCount: 0);

        snapshot.Rows.Should().BeEmpty();
        snapshot.HasMore.Should().BeFalse();
    }
}
