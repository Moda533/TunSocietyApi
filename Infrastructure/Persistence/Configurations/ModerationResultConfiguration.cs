using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class ModerationResultConfiguration : IEntityTypeConfiguration<ModerationResult>
{
    public void Configure(EntityTypeBuilder<ModerationResult> builder)
    {
        builder.HasKey(result => result.Id);

        builder.Property(result => result.ContentType)
            .HasMaxLength(64);

        builder.Property(result => result.ContentSnapshot)
            .HasMaxLength(4000);

        builder.Property(result => result.Action)
            .HasMaxLength(32);

        builder.Property(result => result.EscalationNote)
            .HasMaxLength(1000);

        builder.Property(result => result.FlagsData)
            .HasMaxLength(512);

        builder.HasIndex(result => new { result.ContentType, result.ContentId })
            .IsUnique();

        builder.HasIndex(result => new { result.UserId, result.CreatedAtUtc });
        builder.HasIndex(result => new { result.IsEscalated, result.EscalatedAtUtc, result.CreatedAtUtc });

        builder.HasOne(result => result.User)
            .WithMany()
            .HasForeignKey(result => result.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
