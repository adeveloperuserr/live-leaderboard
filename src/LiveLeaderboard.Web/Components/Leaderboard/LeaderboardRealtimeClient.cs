using LiveLeaderboard.Core.Leaderboards;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveLeaderboard.Web.Components.Leaderboard;

public sealed class LeaderboardRealtimeClient(NavigationManager navigation) : IAsyncDisposable
{
    private HubConnection? connection;

    public event Func<LeaderboardSnapshot, Task>? SnapshotReceived;

    public async Task StartAsync(
        Guid competitionId,
        Func<Task> reloadAfterReconnect,
        CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return;
        }

        connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("/hubs/leaderboard"))
            .WithAutomaticReconnect()
            .Build();

        connection.On<LeaderboardSnapshot>("leaderboardUpdated", async snapshot =>
        {
            if (SnapshotReceived is not null)
            {
                await SnapshotReceived(snapshot);
            }
        });
        connection.Reconnected += async _ => await reloadAfterReconnect();

        await connection.StartAsync(cancellationToken);
        var joined = await connection.InvokeAsync<bool>("JoinCompetition", competitionId, cancellationToken);
        if (!joined)
        {
            throw new InvalidOperationException("The competition is not public.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }
    }
}
