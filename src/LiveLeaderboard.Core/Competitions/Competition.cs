namespace LiveLeaderboard.Core.Competitions;

public sealed class Competition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CompetitionStatus Status { get; set; } = CompetitionStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Participant> Participants { get; set; } = new List<Participant>();
}
