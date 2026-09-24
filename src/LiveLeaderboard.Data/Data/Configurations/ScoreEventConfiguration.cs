using LiveLeaderboard.Core.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiveLeaderboard.Data.Data.Configurations;

public sealed class ScoreEventConfiguration : IEntityTypeConfiguration<ScoreEvent>
{
    public void Configure(EntityTypeBuilder<ScoreEvent> builder)
    {
        builder.HasKey(scoreEvent => scoreEvent.Id);
        builder.Property(scoreEvent => scoreEvent.Reason).HasMaxLength(500).IsRequired();
        builder.Property(scoreEvent => scoreEvent.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(scoreEvent => scoreEvent.SubmittedByUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(scoreEvent => new { scoreEvent.CompetitionId, scoreEvent.IdempotencyKey }).IsUnique();
        builder.HasIndex(scoreEvent => new { scoreEvent.CompetitionId, scoreEvent.CreatedAtUtc });

        builder.HasOne(scoreEvent => scoreEvent.Competition)
            .WithMany()
            .HasForeignKey(scoreEvent => scoreEvent.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(scoreEvent => scoreEvent.Participant)
            .WithMany(participant => participant.ScoreEvents)
            .HasForeignKey(scoreEvent => scoreEvent.ParticipantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
