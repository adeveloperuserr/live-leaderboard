using System.Text.RegularExpressions;
using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Data.Data;
using LiveLeaderboard.Data.Redis;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Administration;

public sealed record AdminCompetitionView(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    CompetitionStatus Status,
    int ParticipantCount);

public sealed record AdminParticipantView(
    Guid Id,
    Guid CompetitionId,
    string DisplayName,
    string? Handle,
    int SortOrder,
    bool IsActive,
    int TotalScore,
    bool HasScoreHistory);

public sealed record ProjectionHealthView(
    Guid CompetitionId,
    string CompetitionName,
    CompetitionStatus CompetitionStatus,
    bool RedisAvailable,
    int PendingCount,
    int ProcessingCount,
    int FailedCount,
    DateTime? LastAttemptAtUtc,
    string? LastError);

public sealed class AdminLeaderboardService(
    ApplicationDbContext db,
    RedisLeaderboardStore redis,
    RedisProjectionRebuilder rebuilder)
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<AdminCompetitionView>> GetCompetitionsAsync(CancellationToken cancellationToken = default)
    {
        return await db.Competitions
            .AsNoTracking()
            .OrderByDescending(competition => competition.UpdatedAtUtc)
            .Select(competition => new AdminCompetitionView(
                competition.Id,
                competition.Slug,
                competition.Name,
                competition.Description,
                competition.Status,
                competition.Participants.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminCompetitionView> CreateCompetitionAsync(
        string slug,
        string name,
        string? description,
        CompetitionStatus status,
        CancellationToken cancellationToken = default)
    {
        slug = slug.Trim().ToLowerInvariant();
        name = name.Trim();
        if (!SlugPattern.IsMatch(slug))
        {
            throw new InvalidOperationException("Slug must use lowercase letters, numbers, and single hyphens.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A competition name is required.");
        }

        var competition = new Competition
        {
            Slug = slug,
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = status,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync(cancellationToken);

        return new AdminCompetitionView(competition.Id, competition.Slug, competition.Name, competition.Description, competition.Status, 0);
    }

    public async Task SetCompetitionStatusAsync(Guid competitionId, CompetitionStatus status, CancellationToken cancellationToken = default)
    {
        var competition = await db.Competitions.SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken)
            ?? throw new InvalidOperationException("Competition not found.");
        competition.Status = status;
        competition.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminParticipantView>> GetParticipantsAsync(Guid competitionId, CancellationToken cancellationToken = default)
    {
        return await db.Participants
            .AsNoTracking()
            .Where(participant => participant.CompetitionId == competitionId)
            .OrderBy(participant => participant.SortOrder)
            .ThenBy(participant => participant.DisplayName)
            .Select(participant => new AdminParticipantView(
                participant.Id,
                participant.CompetitionId,
                participant.DisplayName,
                participant.Handle,
                participant.SortOrder,
                participant.IsActive,
                participant.Total == null ? 0 : participant.Total.TotalScore,
                participant.ScoreEvents.Any()))
            .ToListAsync(cancellationToken);
    }

    public async Task AddParticipantAsync(
        Guid competitionId,
        string displayName,
        string? handle,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("A participant name is required.");
        }

        var competitionExists = await db.Competitions.AnyAsync(item => item.Id == competitionId, cancellationToken);
        if (!competitionExists)
        {
            throw new InvalidOperationException("Competition not found.");
        }

        db.Participants.Add(new Participant
        {
            CompetitionId = competitionId,
            DisplayName = displayName.Trim(),
            Handle = string.IsNullOrWhiteSpace(handle) ? null : handle.Trim().TrimStart('@'),
            SortOrder = sortOrder,
            IsActive = true
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        var participant = await db.Participants.SingleOrDefaultAsync(item => item.Id == participantId, cancellationToken)
            ?? throw new InvalidOperationException("Participant not found.");
        participant.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        await redis.RemoveAsync(participant.CompetitionId, participant.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectionHealthView>> GetProjectionHealthAsync(CancellationToken cancellationToken = default)
    {
        var competitions = await db.Competitions
            .AsNoTracking()
            .OrderBy(competition => competition.Name)
            .ToListAsync(cancellationToken);

        var result = new List<ProjectionHealthView>(competitions.Count);
        foreach (var competition in competitions)
        {
            var work = await db.ProjectionOutboxes
                .AsNoTracking()
                .Where(item => item.CompetitionId == competition.Id)
                .ToListAsync(cancellationToken);
            result.Add(new ProjectionHealthView(
                competition.Id,
                competition.Name,
                competition.Status,
                await redis.ExistsAsync(competition.Id, cancellationToken),
                work.Count(item => item.Status == ProjectionStatus.Pending),
                work.Count(item => item.Status == ProjectionStatus.Processing),
                work.Count(item => item.Status == ProjectionStatus.Failed),
                work.OrderByDescending(item => item.LastAttemptAtUtc).FirstOrDefault()?.LastAttemptAtUtc,
                work.OrderByDescending(item => item.LastAttemptAtUtc).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.LastError))?.LastError));
        }

        return result;
    }

    public async Task RebuildProjectionAsync(Guid competitionId, CancellationToken cancellationToken = default)
    {
        var exists = await db.Competitions.AnyAsync(item => item.Id == competitionId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Competition not found.");
        }

        await rebuilder.RebuildAsync(competitionId, cancellationToken);
    }
}
