using LiveLeaderboard.Core.Competitions;

namespace LiveLeaderboard.Core.Scoring;

public sealed class ParticipantTotal
{
    public Guid CompetitionId { get; set; }
    public Guid ParticipantId { get; set; }
    public int TotalScore { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Competition? Competition { get; set; }
    public Participant? Participant { get; set; }
}
