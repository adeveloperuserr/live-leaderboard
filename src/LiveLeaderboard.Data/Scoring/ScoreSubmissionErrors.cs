namespace LiveLeaderboard.Data.Scoring;

public static class ScoreSubmissionErrors
{
    public const string CompetitionNotLive = "CompetitionNotLive";
    public const string CompetitionNotFound = "CompetitionNotFound";
    public const string ParticipantInactive = "ParticipantInactive";
    public const string ParticipantNotFound = "ParticipantNotFound";
    public const string InvalidPoints = "InvalidPoints";
    public const string InvalidReason = "InvalidReason";
    public const string InvalidIdempotencyKey = "InvalidIdempotencyKey";
    public const string IdempotencyConflict = "IdempotencyConflict";
}

public sealed record ScoreSubmissionValidationError(string ErrorCode, string ErrorMessage);

public static class ScoreSubmissionValidator
{
    public static ScoreSubmissionValidationError? Validate(
        Core.Competitions.CompetitionStatus competitionStatus,
        bool participantIsActive,
        int points,
        string? reason,
        string? idempotencyKey)
    {
        if (competitionStatus != Core.Competitions.CompetitionStatus.Live)
        {
            return new(ScoreSubmissionErrors.CompetitionNotLive, "Scores can only be submitted while a competition is live.");
        }

        if (!participantIsActive)
        {
            return new(ScoreSubmissionErrors.ParticipantInactive, "The participant is not active.");
        }

        if (points == 0)
        {
            return new(ScoreSubmissionErrors.InvalidPoints, "A score adjustment cannot be zero.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            return new(ScoreSubmissionErrors.InvalidReason, "A reason between 1 and 500 characters is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 160)
        {
            return new(ScoreSubmissionErrors.InvalidIdempotencyKey, "A non-empty idempotency key of at most 160 characters is required.");
        }

        return null;
    }
}
