using Bunit;
using FluentAssertions;
using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Web.Components.Leaderboard;
using Microsoft.AspNetCore.Components;

namespace LiveLeaderboard.Tests.Web;

public sealed class PublicLeaderboardTests : BunitContext
{
    [Fact]
    public void Table_shows_stable_tied_ranks_names_handles_scores_and_last_updated()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var snapshot = new LeaderboardSnapshot(
            [
                new LeaderboardRow(firstId, "Ada Lovelace", 100, 1, 0, "ada", null),
                new LeaderboardRow(secondId, "Grace Hopper", 100, 1, 1, "grace", 1),
                new LeaderboardRow(Guid.NewGuid(), "Katherine Johnson", 75, 3, 2, "kat", -1)
            ],
            Page: 1,
            PageSize: 50,
            TotalCount: 3,
            HasMore: false,
            ProjectedAtUtc: DateTime.UtcNow);

        var cut = Render<LeaderboardTable>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.IsLive, true));

        cut.Markup.Should().Contain("#1");
        cut.Markup.Should().Contain("#3");
        cut.Markup.Should().Contain("Ada Lovelace");
        cut.Markup.Should().Contain("@grace");
        cut.Markup.Should().Contain("100");
        cut.Markup.Should().Contain("Updated");
    }

    [Fact]
    public void Table_shows_empty_competition_state()
    {
        var snapshot = new LeaderboardSnapshot([], 1, 50, 0, false, DateTime.UtcNow);

        var cut = Render<LeaderboardTable>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.IsLive, true));

        cut.Find("[data-testid='empty-state']").TextContent.Should().Contain("No scores yet");
    }

    [Fact]
    public void Table_shows_load_more_when_projection_has_another_page()
    {
        var snapshot = new LeaderboardSnapshot(
            [new LeaderboardRow(Guid.NewGuid(), "Ada Lovelace", 100, 1, 0, "ada", null)],
            1,
            50,
            51,
            true,
            DateTime.UtcNow);

        var cut = Render<LeaderboardTable>(parameters => parameters
            .Add(component => component.Snapshot, snapshot)
            .Add(component => component.IsLive, false)
            .Add(component => component.OnLoadMore, EventCallback.Factory.Create(this, () => { })));

        cut.Find("[data-testid='load-more']").TextContent.Should().Contain("Load more");
    }

    [Fact]
    public void Projection_status_shows_unavailable_state_without_hiding_the_reason()
    {
        var cut = Render<ProjectionStatus>(parameters => parameters
            .Add(component => component.IsAvailable, false)
            .Add(component => component.IsLive, true)
            .Add(component => component.ErrorMessage, "Redis projection is warming up."));

        cut.Find("[data-testid='projection-unavailable']").TextContent
            .Should().Contain("Redis projection is warming up.");
    }

    [Fact]
    public void Projection_status_distinguishes_live_and_completed_competitions()
    {
        var live = Render<ProjectionStatus>(parameters => parameters
            .Add(component => component.IsAvailable, true)
            .Add(component => component.IsLive, true)
            .Add(component => component.ProjectedAtUtc, DateTime.UtcNow));
        live.Find("[data-testid='live-status']").TextContent.Should().Contain("LIVE");

        var completed = Render<ProjectionStatus>(parameters => parameters
            .Add(component => component.IsAvailable, true)
            .Add(component => component.IsLive, false)
            .Add(component => component.ProjectedAtUtc, DateTime.UtcNow));
        completed.Find("[data-testid='completed-status']").TextContent.Should().Contain("FINAL");
    }
}
