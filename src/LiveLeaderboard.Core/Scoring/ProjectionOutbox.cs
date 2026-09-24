namespace LiveLeaderboard.Core.Scoring;

public sealed class ProjectionOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScoreEventId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ParticipantId { get; set; }
    public int TotalScore { get; set; }
    public ProjectionStatus Status { get; set; } = ProjectionStatus.Pending;
    public int Attempts { get; set; }
    public DateTime AvailableAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntilUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}
