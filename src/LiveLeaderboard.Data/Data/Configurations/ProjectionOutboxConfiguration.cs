using LiveLeaderboard.Core.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiveLeaderboard.Data.Data.Configurations;

public sealed class ProjectionOutboxConfiguration : IEntityTypeConfiguration<ProjectionOutbox>
{
    public void Configure(EntityTypeBuilder<ProjectionOutbox> builder)
    {
        builder.HasKey(outbox => outbox.Id);
        builder.Property(outbox => outbox.Status).IsRequired();
        builder.Property(outbox => outbox.LastError).HasMaxLength(2000);
        builder.HasIndex(outbox => new { outbox.Status, outbox.AvailableAtUtc, outbox.LeaseUntilUtc });
        builder.HasIndex(outbox => outbox.ScoreEventId).IsUnique();
    }
}
