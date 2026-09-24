using LiveLeaderboard.Core.Competitions;

namespace LiveLeaderboard.Core.Scoring;

public sealed class ScoreEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    public Guid ParticipantId { get; set; }
    public int Points { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SubmittedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Competition? Competition { get; set; }
    public Participant? Participant { get; set; }
}
