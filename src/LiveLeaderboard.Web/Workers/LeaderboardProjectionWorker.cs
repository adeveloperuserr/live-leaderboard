using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Scoring;
using Microsoft.Extensions.DependencyInjection;

namespace LiveLeaderboard.Web.Workers;

public sealed class LeaderboardProjectionWorker(
    IProjectionWorkStore workStore,
    IServiceScopeFactory scopeFactory,
    IProjectionNotifier notifier,
    ILogger<LeaderboardProjectionWorker> logger) : BackgroundService
{
    public async Task ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var batch = await workStore.ClaimBatchAsync(
            DateTime.UtcNow,
            batchSize: 20,
            lease: TimeSpan.FromSeconds(30),
            cancellationToken);

        foreach (var work in batch)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var projector = scope.ServiceProvider.GetRequiredService<ILeaderboardProjector>();
            await ProcessWorkItemAsync(work, projector, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The leaderboard projection loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task ProcessWorkItemAsync(
        ProjectionOutbox work,
        ILeaderboardProjector projector,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await projector.ProjectAsync(work, cancellationToken);
            await notifier.PublishAsync(snapshot, cancellationToken);
            await workStore.MarkPublishedAsync(work.Id, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Projection work {ProjectionWorkId} failed on attempt {Attempt}.", work.Id, work.Attempts);
            await workStore.MarkRetryAsync(work.Id, exception.Message, DateTime.UtcNow, cancellationToken);
        }
    }
}
