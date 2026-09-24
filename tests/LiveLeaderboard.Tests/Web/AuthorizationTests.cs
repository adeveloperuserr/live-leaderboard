using FluentAssertions;
using LiveLeaderboard.Web.Authorization;
using LiveLeaderboard.Web.Components.Pages;
using LiveLeaderboard.Web.Components.Pages.Admin;
using Microsoft.AspNetCore.Authorization;

namespace LiveLeaderboard.Tests.Web;

public sealed class AuthorizationTests
{
    [Fact]
    public void Public_leaderboard_route_does_not_require_authentication()
    {
        typeof(Leaderboard).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void Scoring_route_requires_scorer_or_admin()
    {
        var authorization = typeof(LiveLeaderboard.Web.Components.Pages.Scoring)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        authorization.Roles.Should().Be(LeaderboardPolicies.ScorerOrAdminRoles);
    }

    [Fact]
    public void Admin_routes_require_admin_role()
    {
        foreach (var page in new[]
        {
            typeof(Competitions),
            typeof(Participants),
            typeof(ProjectionHealth)
        })
        {
            var authorization = page
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .Single();

            authorization.Roles.Should().Be(LeaderboardPolicies.AdminRole);
        }
    }
}
