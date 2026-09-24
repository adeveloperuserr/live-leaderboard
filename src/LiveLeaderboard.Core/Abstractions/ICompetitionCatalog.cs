using LiveLeaderboard.Core.Competitions;

namespace LiveLeaderboard.Core.Abstractions;

public interface ICompetitionCatalog
{
    Task<Competition?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
