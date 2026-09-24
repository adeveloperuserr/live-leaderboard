namespace LiveLeaderboard.Core.Competitions;

public sealed class Participant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    public Competition? Competition { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Scoring.ScoreEvent> ScoreEvents { get; set; } = new List<Scoring.ScoreEvent>();
    public Scoring.ParticipantTotal? Total { get; set; }
}
