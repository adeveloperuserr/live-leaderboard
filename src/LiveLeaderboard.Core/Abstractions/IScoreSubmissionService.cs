using LiveLeaderboard.Core.Scoring;

namespace LiveLeaderboard.Core.Abstractions;

public interface IScoreSubmissionService
{
    Task<ScoreSubmissionResult> SubmitScoreAsync(
        Guid competitionId,
        Guid participantId,
        int points,
        string reason,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken = default);
}
