using FluentAssertions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Web.Realtime;

namespace LiveLeaderboard.Tests.Realtime;

public sealed class LeaderboardHubTests
{
    [Theory]
    [InlineData(CompetitionStatus.Live, true)]
    [InlineData(CompetitionStatus.Completed, true)]
    [InlineData(CompetitionStatus.Draft, false)]
    public void Only_public_competition_statuses_can_join(CompetitionStatus status, bool expected)
    {
        LeaderboardHub.IsPublic(status).Should().Be(expected);
    }

    [Fact]
    public void Competition_group_names_are_isolated()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        LeaderboardGroupNames.ForCompetition(first).Should().Be($"competition:{first:N}");
        LeaderboardGroupNames.ForCompetition(second).Should().NotBe(LeaderboardGroupNames.ForCompetition(first));
    }
}
