using FluentAssertions;
using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Web.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLeaderboard.Tests.Workers;

public sealed class LeaderboardProjectionWorkerTests
{
    [Fact]
    public async Task A_successful_projection_is_published_and_marked_complete()
    {
        var work = PendingWork();
        var store = new FakeProjectionWorkStore(work);
        var notifier = new FakeNotifier();
        await using var services = ProjectorServices(new FakeProjector(Snapshot()));
        var worker = CreateWorker(store, services, notifier);

        await worker.ProcessOnceAsync();

        store.PublishedIds.Should().Contain(work.Id);
        store.RetryCalls.Should().BeEmpty();
        notifier.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task A_notifier_failure_leaves_the_work_retryable()
    {
        var work = PendingWork();
        var store = new FakeProjectionWorkStore(work);
        await using var services = ProjectorServices(new FakeProjector(Snapshot()));
        var worker = CreateWorker(store, services, new FakeNotifier(new InvalidOperationException("SignalR unavailable")));

        await worker.ProcessOnceAsync();

        store.PublishedIds.Should().BeEmpty();
        store.RetryCalls.Should().ContainSingle(call => call.Id == work.Id && call.Error.Contains("SignalR unavailable"));
    }

    [Fact]
    public async Task The_worker_claims_pending_work_once_per_pass()
    {
        var work = PendingWork();
        var store = new FakeProjectionWorkStore(work);
        await using var services = ProjectorServices(new FakeProjector(Snapshot()));
        var worker = CreateWorker(store, services, new FakeNotifier());

        await worker.ProcessOnceAsync();
        await worker.ProcessOnceAsync();

        store.ClaimCount.Should().Be(2);
        store.PublishedIds.Should().ContainSingle();
    }

    private static ProjectionOutbox PendingWork() => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = Guid.NewGuid(),
        ParticipantId = Guid.NewGuid(),
        TotalScore = 25,
        Status = ProjectionStatus.Processing,
        Attempts = 1
    };

    private static LeaderboardSnapshot Snapshot() => new(
        Array.Empty<LeaderboardRow>(),
        Page: 1,
        PageSize: 50,
        TotalCount: 0,
        HasMore: false,
        ProjectedAtUtc: DateTime.UtcNow);

    private static ServiceProvider ProjectorServices(ILeaderboardProjector projector) =>
        new ServiceCollection()
            .AddScoped<ILeaderboardProjector>(_ => projector)
            .BuildServiceProvider();

    private static LeaderboardProjectionWorker CreateWorker(
        IProjectionWorkStore store,
        ServiceProvider services,
        IProjectionNotifier notifier) =>
        new(store, services.GetRequiredService<IServiceScopeFactory>(), notifier, NullLogger<LeaderboardProjectionWorker>.Instance);

    private sealed class FakeProjectionWorkStore(ProjectionOutbox initial) : IProjectionWorkStore
    {
        private bool available = true;
        public int ClaimCount { get; private set; }
        public List<Guid> PublishedIds { get; } = [];
        public List<(Guid Id, string Error)> RetryCalls { get; } = [];

        public Task<IReadOnlyList<ProjectionOutbox>> ClaimBatchAsync(DateTime utcNow, int batchSize, TimeSpan lease, CancellationToken cancellationToken = default)
        {
            ClaimCount++;
            if (!available)
            {
                return Task.FromResult<IReadOnlyList<ProjectionOutbox>>([]);
            }

            available = false;
            return Task.FromResult<IReadOnlyList<ProjectionOutbox>>([initial]);
        }

        public Task MarkPublishedAsync(Guid id, DateTime publishedAtUtc, CancellationToken cancellationToken = default)
        {
            PublishedIds.Add(id);
            return Task.CompletedTask;
        }

        public Task MarkRetryAsync(Guid id, string error, DateTime utcNow, CancellationToken cancellationToken = default)
        {
            RetryCalls.Add((id, error));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjector(LeaderboardSnapshot snapshot) : ILeaderboardProjector
    {
        public Task<LeaderboardSnapshot> ProjectAsync(ProjectionOutbox work, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class FakeNotifier(Exception? failure = null) : IProjectionNotifier
    {
        public List<LeaderboardSnapshot> Published { get; } = [];

        public Task PublishAsync(LeaderboardSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (failure is not null)
            {
                throw failure;
            }

            Published.Add(snapshot);
            return Task.CompletedTask;
        }
    }
}
