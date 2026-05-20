using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(log => log.Action)
            .HasMaxLength(128);

        builder.Property(log => log.Category)
            .HasMaxLength(64);

        builder.Property(log => log.EntityType)
            .HasMaxLength(64);

        builder.Property(log => log.EntityId)
            .HasMaxLength(128);

        builder.Property(log => log.TargetDisplayName)
            .HasMaxLength(256);

        builder.Property(log => log.Summary)
            .HasMaxLength(512);

        builder.HasIndex(log => log.CreatedAtUtc);
        builder.HasIndex(log => new { log.ActorUserId, log.CreatedAtUtc });
        builder.HasIndex(log => new { log.SubjectUserId, log.CreatedAtUtc });
        builder.HasIndex(log => new { log.Category, log.CreatedAtUtc });
        builder.HasIndex(log => new { log.EntityType, log.EntityId });
        builder.HasIndex(log => log.Action);
    }
}
