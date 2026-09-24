using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Scoring;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiveLeaderboard.Data.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<ScoreEvent> ScoreEvents => Set<ScoreEvent>();
    public DbSet<ParticipantTotal> ParticipantTotals => Set<ParticipantTotal>();
    public DbSet<ProjectionOutbox> ProjectionOutboxes => Set<ProjectionOutbox>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
