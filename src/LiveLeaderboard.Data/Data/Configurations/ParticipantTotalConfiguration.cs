using LiveLeaderboard.Core.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiveLeaderboard.Data.Data.Configurations;

public sealed class ParticipantTotalConfiguration : IEntityTypeConfiguration<ParticipantTotal>
{
    public void Configure(EntityTypeBuilder<ParticipantTotal> builder)
    {
        builder.HasKey(total => new { total.CompetitionId, total.ParticipantId });
        builder.Property(total => total.TotalScore).IsRequired();

        builder.HasOne(total => total.Competition)
            .WithMany()
            .HasForeignKey(total => total.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
