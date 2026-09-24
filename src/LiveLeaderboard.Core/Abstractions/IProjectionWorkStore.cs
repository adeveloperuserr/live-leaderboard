using LiveLeaderboard.Core.Scoring;

namespace LiveLeaderboard.Core.Abstractions;

public interface IProjectionWorkStore
{
    Task<IReadOnlyList<ProjectionOutbox>> ClaimBatchAsync(
        DateTime utcNow,
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(Guid id, DateTime publishedAtUtc, CancellationToken cancellationToken = default);

    Task MarkRetryAsync(Guid id, string error, DateTime utcNow, CancellationToken cancellationToken = default);
}
