using FluentAssertions;
using LiveLeaderboard.Data.Redis;
using StackExchange.Redis;

namespace LiveLeaderboard.Tests.Redis;

public sealed class RedisLeaderboardStoreTests : IAsyncLifetime
{
    private readonly Guid competitionId = Guid.NewGuid();
    private readonly Guid firstParticipantId = Guid.NewGuid();
    private readonly Guid secondParticipantId = Guid.NewGuid();
    private ConnectionMultiplexer connection = null!;
    private RedisLeaderboardStore store = null!;

    public async Task InitializeAsync()
    {
        connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false");
        store = new RedisLeaderboardStore(connection);
    }

    public async Task DisposeAsync()
    {
        if (connection.IsConnected)
        {
            await store.RemoveAsync(competitionId, firstParticipantId);
            await store.RemoveAsync(competitionId, secondParticipantId);
        }

        await connection.CloseAsync();
        connection.Dispose();
    }

    [Fact]
    public async Task Absolute_writes_are_ordered_and_safe_to_retry()
    {
        await store.SetAbsoluteScoreAsync(competitionId, firstParticipantId, 10);
        await store.SetAbsoluteScoreAsync(competitionId, secondParticipantId, 20);
        await store.SetAbsoluteScoreAsync(competitionId, firstParticipantId, 10);

        var entries = await store.ReadOrderedAsync(competitionId, 0, 49);

        entries.Select(entry => entry.Member).Should().Equal(secondParticipantId.ToString("N"), firstParticipantId.ToString("N"));
        entries.Select(entry => entry.Score).Should().Equal(20, 10);
        (await store.CountAsync(competitionId)).Should().Be(2);
    }

    [Fact]
    public async Task Removing_a_participant_removes_only_that_sorted_set_member()
    {
        await store.SetAbsoluteScoreAsync(competitionId, firstParticipantId, 10);
        await store.SetAbsoluteScoreAsync(competitionId, secondParticipantId, 20);

        await store.RemoveAsync(competitionId, firstParticipantId);

        var entries = await store.ReadOrderedAsync(competitionId, 0, 49);
        entries.Select(entry => entry.Member).Should().Equal(secondParticipantId.ToString("N"));
    }
}
