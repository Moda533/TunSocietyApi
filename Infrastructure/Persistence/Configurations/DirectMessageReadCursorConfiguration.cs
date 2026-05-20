using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class DirectMessageReadCursorConfiguration : IEntityTypeConfiguration<DirectMessageReadCursor>
{
    public void Configure(EntityTypeBuilder<DirectMessageReadCursor> builder)
    {
        builder.HasKey(cursor => new { cursor.UserId, cursor.PartnerUserId });

        builder.Property(cursor => cursor.UserId)
            .HasColumnType("char(36)");

        builder.Property(cursor => cursor.PartnerUserId)
            .HasColumnType("char(36)");

        builder.Property(cursor => cursor.LastVisibleMessageId)
            .HasColumnType("char(36)");

        builder.Property(cursor => cursor.LastVisibleMessageAtUtc)
            .HasColumnType("datetime(6)");

        builder.Property(cursor => cursor.UpdatedAtUtc)
            .HasColumnType("datetime(6)");

        builder.HasIndex(cursor => cursor.PartnerUserId);

        builder.HasOne(cursor => cursor.User)
            .WithMany()
            .HasForeignKey(cursor => cursor.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cursor => cursor.PartnerUser)
            .WithMany()
            .HasForeignKey(cursor => cursor.PartnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
