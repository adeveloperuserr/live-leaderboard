using FluentAssertions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Data.Data;
using LiveLeaderboard.Data.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LiveLeaderboard.Tests.Redis;

[Collection(LiveLeaderboard.Tests.Integration.SqlServerCollection.Name)]
public sealed class RedisProjectionRebuilderTests(LiveLeaderboard.Tests.Integration.SqlServerFixture sqlFixture) : IAsyncLifetime
{
    private ConnectionMultiplexer connection = null!;
    private RedisLeaderboardStore store = null!;

    public async Task InitializeAsync()
    {
        connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false");
        store = new RedisLeaderboardStore(connection);
    }

    public async Task DisposeAsync()
    {
        await connection.CloseAsync();
        connection.Dispose();
    }

    [Fact]
    public async Task Missing_projection_is_reported_as_unavailable()
    {
        await using var context = sqlFixture.CreateContext();
        var reader = new RedisLeaderboardReader(context, store);

        var result = await reader.GetSnapshotAsync(Guid.NewGuid(), page: 1, pageSize: 50);

        result.IsAvailable.Should().BeFalse();
        result.Snapshot.Should().BeNull();
    }

    [Fact]
    public async Task Rebuild_swaps_a_complete_projection_and_removes_stale_members()
    {
        var competition = new Competition
        {
            Slug = $"redis-{Guid.NewGuid():N}",
            Name = "Redis test",
            Status = CompetitionStatus.Live
        };
        var first = new Participant { CompetitionId = competition.Id, DisplayName = "First", SortOrder = 1 };
        var second = new Participant { CompetitionId = competition.Id, DisplayName = "Second", SortOrder = 2 };
        competition.Participants.Add(first);
        competition.Participants.Add(second);

        await using (var context = sqlFixture.CreateContext())
        {
            context.Competitions.Add(competition);
            context.ParticipantTotals.AddRange(
                new ParticipantTotal { CompetitionId = competition.Id, ParticipantId = first.Id, TotalScore = 5 },
                new ParticipantTotal { CompetitionId = competition.Id, ParticipantId = second.Id, TotalScore = 25 });
            await context.SaveChangesAsync();
        }

        await store.SetAbsoluteScoreAsync(competition.Id, Guid.NewGuid(), 999);
        var rebuilder = new RedisProjectionRebuilder(sqlFixture.CreateContext(), store);

        await rebuilder.RebuildAsync(competition.Id);

        var entries = await store.ReadOrderedAsync(competition.Id, 0, 49);
        entries.Select(entry => entry.Member).Should().Equal(second.Id.ToString("N"), first.Id.ToString("N"));
        entries.Select(entry => entry.Score).Should().Equal(25, 5);
    }
}
