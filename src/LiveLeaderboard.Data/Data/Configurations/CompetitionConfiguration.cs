using LiveLeaderboard.Core.Competitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiveLeaderboard.Data.Data.Configurations;

public sealed class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.HasKey(competition => competition.Id);
        builder.Property(competition => competition.Slug).HasMaxLength(80).IsRequired();
        builder.Property(competition => competition.Name).HasMaxLength(160).IsRequired();
        builder.Property(competition => competition.Description).HasMaxLength(2000);
        builder.HasIndex(competition => competition.Slug).IsUnique();

        builder.HasMany(competition => competition.Participants)
            .WithOne(participant => participant.Competition)
            .HasForeignKey(participant => participant.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
