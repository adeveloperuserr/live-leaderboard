namespace LiveLeaderboard.Core.Scoring;

public sealed record ScoreSubmissionResult(
    bool Succeeded,
    Guid? ScoreEventId,
    Guid ParticipantId,
    int? NewTotalScore,
    bool WasReplayed,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ScoreSubmissionResult Success(Guid eventId, Guid participantId, int newTotalScore, bool wasReplayed = false) =>
        new(true, eventId, participantId, newTotalScore, wasReplayed, null, null);

    public static ScoreSubmissionResult Failure(Guid participantId, string errorCode, string errorMessage) =>
        new(false, null, participantId, null, false, errorCode, errorMessage);
}
