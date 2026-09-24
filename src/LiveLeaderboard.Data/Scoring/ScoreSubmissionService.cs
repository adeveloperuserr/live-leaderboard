using System.Data;
using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Core.Scoring;
using LiveLeaderboard.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Scoring;

public sealed class ScoreSubmissionService(ApplicationDbContext db) : IScoreSubmissionService
{
    public async Task<ScoreSubmissionResult> SubmitScoreAsync(
        Guid competitionId,
        Guid participantId,
        int points,
        string reason,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var competition = await db.Competitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == competitionId, cancellationToken);
        if (competition is null)
        {
            return ScoreSubmissionResult.Failure(participantId, ScoreSubmissionErrors.CompetitionNotFound, "Competition not found.");
        }

        var participant = await db.Participants
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == participantId && item.CompetitionId == competitionId, cancellationToken);
        if (participant is null)
        {
            return ScoreSubmissionResult.Failure(participantId, ScoreSubmissionErrors.ParticipantNotFound, "Participant not found.");
        }

        var validationError = ScoreSubmissionValidator.Validate(
            competition.Status,
            participant.IsActive,
            points,
            reason,
            idempotencyKey);
        if (validationError is not null)
        {
            return ScoreSubmissionResult.Failure(participantId, validationError.ErrorCode, validationError.ErrorMessage);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var existing = await db.ScoreEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (existing.ParticipantId != participantId || existing.Points != points || existing.Reason != reason)
            {
                return ScoreSubmissionResult.Failure(participantId, ScoreSubmissionErrors.IdempotencyConflict, "The idempotency key was already used for a different score.");
            }

            return ScoreSubmissionResult.Success(existing.Id, existing.ParticipantId, existing.TotalAfterScore, wasReplayed: true);
        }

        var total = await db.ParticipantTotals
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.ParticipantId == participantId, cancellationToken);
        if (total is null)
        {
            total = new ParticipantTotal
            {
                CompetitionId = competitionId,
                ParticipantId = participantId,
                TotalScore = points,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.ParticipantTotals.Add(total);
        }
        else
        {
            checked
            {
                total.TotalScore += points;
            }

            total.UpdatedAtUtc = DateTime.UtcNow;
        }

        var scoreEvent = new ScoreEvent
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            Points = points,
            TotalAfterScore = total.TotalScore,
            Reason = reason.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            SubmittedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ScoreEvents.Add(scoreEvent);
        db.ProjectionOutboxes.Add(new ProjectionOutbox
        {
            ScoreEventId = scoreEvent.Id,
            CompetitionId = competitionId,
            ParticipantId = participantId,
            TotalScore = total.TotalScore,
            Status = ProjectionStatus.Pending,
            AvailableAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var racedEvent = await db.ScoreEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (racedEvent is not null)
            {
                if (racedEvent.ParticipantId != participantId || racedEvent.Points != points || racedEvent.Reason != reason)
                {
                    return ScoreSubmissionResult.Failure(participantId, ScoreSubmissionErrors.IdempotencyConflict, "The idempotency key was already used for a different score.");
                }

                return ScoreSubmissionResult.Success(racedEvent.Id, racedEvent.ParticipantId, racedEvent.TotalAfterScore, wasReplayed: true);
            }

            throw;
        }

        return ScoreSubmissionResult.Success(scoreEvent.Id, participantId, total.TotalScore);
    }
}
