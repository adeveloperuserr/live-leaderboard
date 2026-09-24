using LiveLeaderboard.Core.Competitions;
using LiveLeaderboard.Core.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiveLeaderboard.Data.Data.Configurations;

public sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.HasKey(participant => participant.Id);
        builder.Property(participant => participant.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(participant => participant.Handle).HasMaxLength(80);
        builder.HasIndex(participant => new { participant.CompetitionId, participant.DisplayName }).IsUnique();
        builder.HasIndex(participant => new { participant.CompetitionId, participant.IsActive, participant.SortOrder });

        builder.HasOne(participant => participant.Total)
            .WithOne(total => total.Participant)
            .HasForeignKey<ParticipantTotal>(total => total.ParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
