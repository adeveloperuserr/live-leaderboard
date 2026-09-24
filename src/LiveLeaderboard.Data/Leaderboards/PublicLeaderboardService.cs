using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Leaderboards;
using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Leaderboards;

public sealed record PublicCompetitionView(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    CompetitionStatus Status)
{
    public bool IsLive => Status == CompetitionStatus.Live;
}

public sealed record PublicScoreEventView(
    int Points,
    int TotalAfterScore,
    string Reason,
    DateTime CreatedAtUtc);

public sealed record PublicParticipantView(
    Guid Id,
    string DisplayName,
    string? Handle,
    int Score,
    int? Rank,
    IReadOnlyList<PublicScoreEventView> RecentEvents);

public sealed class PublicLeaderboardService(
    ApplicationDbContext db,
    ILeaderboardReader reader) : ICompetitionCatalog
{
    public async Task<Competition?> GetPublicBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return await db.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                competition => competition.Slug == slug && competition.Status != CompetitionStatus.Draft,
                cancellationToken);
    }

    public async Task<PublicCompetitionView?> GetPublicCompetitionAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return await db.Competitions
            .AsNoTracking()
            .Where(competition => competition.Slug == slug && competition.Status != CompetitionStatus.Draft)
            .Select(competition => new PublicCompetitionView(
                competition.Id,
                competition.Slug,
                competition.Name,
                competition.Description,
                competition.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublicCompetitionView>> GetPublicCompetitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.Competitions
            .AsNoTracking()
            .Where(competition => competition.Status != CompetitionStatus.Draft)
            .OrderByDescending(competition => competition.Status == CompetitionStatus.Live)
            .ThenByDescending(competition => competition.UpdatedAtUtc)
            .Select(competition => new PublicCompetitionView(
                competition.Id,
                competition.Slug,
                competition.Name,
                competition.Description,
                competition.Status))
            .ToListAsync(cancellationToken);
    }

    public Task<LeaderboardReadResult> GetSnapshotAsync(
        Guid competitionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        reader.GetSnapshotAsync(competitionId, page, pageSize, cancellationToken);

    public async Task<PublicParticipantView?> GetParticipantAsync(
        PublicCompetitionView competition,
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        var participant = await db.Participants
            .AsNoTracking()
            .Where(candidate => candidate.Id == participantId
                && candidate.CompetitionId == competition.Id
                && candidate.IsActive)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.DisplayName,
                candidate.Handle,
                Score = candidate.Total == null ? 0 : candidate.Total.TotalScore
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (participant is null)
        {
            return null;
        }

        var projection = await reader.GetSnapshotAsync(competition.Id, 1, 100, cancellationToken);
        var rank = projection.Snapshot?.Rows
            .FirstOrDefault(row => row.ParticipantId == participant.Id)
            ?.Rank;

        var events = await db.ScoreEvents
            .AsNoTracking()
            .Where(scoreEvent => scoreEvent.CompetitionId == competition.Id
                && scoreEvent.ParticipantId == participant.Id)
            .OrderByDescending(scoreEvent => scoreEvent.CreatedAtUtc)
            .Take(10)
            .Select(scoreEvent => new PublicScoreEventView(
                scoreEvent.Points,
                scoreEvent.TotalAfterScore,
                scoreEvent.Reason,
                scoreEvent.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PublicParticipantView(
            participant.Id,
            participant.DisplayName,
            participant.Handle,
            participant.Score,
            rank,
            events);
    }
}
