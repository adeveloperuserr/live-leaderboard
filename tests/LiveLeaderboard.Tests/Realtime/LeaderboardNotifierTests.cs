using FluentAssertions;
using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Web.Realtime;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace LiveLeaderboard.Tests.Realtime;

public sealed class LeaderboardNotifierTests
{
    [Fact]
    public async Task Notifier_sends_the_complete_snapshot_to_the_competition_group()
    {
        var competitionId = Guid.NewGuid();
        var snapshot = new LeaderboardSnapshot(
            Array.Empty<LeaderboardRow>(), 1, 50, 0, false, DateTime.UtcNow)
        {
            CompetitionId = competitionId
        };
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(item => item.Group(LeaderboardGroupNames.ForCompetition(competitionId))).Returns(proxy.Object);
        var hubContext = new Mock<IHubContext<LeaderboardHub>>();
        hubContext.SetupGet(item => item.Clients).Returns(clients.Object);
        var notifier = new LeaderboardNotifier(hubContext.Object);

        await notifier.PublishAsync(snapshot);

        proxy.Verify(item => item.SendCoreAsync(
                "leaderboardUpdated",
                It.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], snapshot)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
