using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class GroupConversationConfiguration : IEntityTypeConfiguration<GroupConversation>
{
    public void Configure(EntityTypeBuilder<GroupConversation> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(160);
        builder.Property(item => item.AvatarUrl).HasColumnType("longtext");
        builder.Property(item => item.Introduction).HasMaxLength(1000);
        builder.Property(item => item.Notice).HasMaxLength(1000);
        builder.Property(item => item.CreateRoomPermission).HasMaxLength(32);
        builder.Property(item => item.InviteCode).HasMaxLength(48);
        builder.Property(item => item.SourceEventId).HasColumnType("char(36)");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.DeletedAtUtc).HasColumnType("datetime(6)");

        builder.HasIndex(item => item.CreatedByUserId);
        builder.HasIndex(item => item.SourceEventId);
        builder.HasIndex(item => item.InviteCode).IsUnique();

        builder.HasOne(item => item.CreatedByUser)
            .WithMany()
            .HasForeignKey(item => item.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
