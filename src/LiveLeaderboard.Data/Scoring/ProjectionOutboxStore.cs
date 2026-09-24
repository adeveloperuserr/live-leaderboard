using System.Data;
using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiveLeaderboard.Data.Scoring;

public sealed class ProjectionOutboxStore(IServiceScopeFactory scopeFactory) : IProjectionWorkStore
{
    public async Task<IReadOnlyList<ProjectionOutbox>> ClaimBatchAsync(
        DateTime utcNow,
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var work = await db.ProjectionOutboxes
            .Where(item =>
                (item.Status == ProjectionStatus.Pending && item.AvailableAtUtc <= utcNow) ||
                (item.Status == ProjectionStatus.Processing && item.LeaseUntilUtc <= utcNow))
            .OrderBy(item => item.AvailableAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);

        foreach (var item in work)
        {
            item.Status = ProjectionStatus.Processing;
            item.Attempts++;
            item.LastAttemptAtUtc = utcNow;
            item.LeaseUntilUtc = utcNow.Add(lease);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }

    public async Task MarkPublishedAsync(Guid id, DateTime publishedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.ProjectionOutboxes.SingleAsync(work => work.Id == id, cancellationToken);
        item.Status = ProjectionStatus.Published;
        item.PublishedAtUtc = publishedAtUtc;
        item.LeaseUntilUtc = null;
        item.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRetryAsync(Guid id, string error, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.ProjectionOutboxes.SingleAsync(work => work.Id == id, cancellationToken);
        item.Status = ProjectionRetryPolicy.ShouldFail(item.Attempts)
            ? ProjectionStatus.Failed
            : ProjectionStatus.Pending;
        item.AvailableAtUtc = utcNow.Add(ProjectionRetryPolicy.GetDelay(item.Attempts));
        item.LeaseUntilUtc = null;
        item.LastError = error.Length <= 2000 ? error : error[..2000];
        await db.SaveChangesAsync(cancellationToken);
    }
}
