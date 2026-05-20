using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class GroupConversationMemberConfiguration : IEntityTypeConfiguration<GroupConversationMember>
{
    public void Configure(EntityTypeBuilder<GroupConversationMember> builder)
    {
        builder.HasKey(item => new { item.GroupConversationId, item.UserId });
        builder.Property(item => item.Role).HasMaxLength(32);
        builder.Property(item => item.Status).HasMaxLength(32);
        builder.Property(item => item.LastReadMessageId).HasColumnType("char(36)");
        builder.Property(item => item.LastReadAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.InvitedByUserId).HasColumnType("char(36)");
        builder.Property(item => item.InvitedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.JoinedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.LeftAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.ClearedAtUtc).HasColumnType("datetime(6)");

        builder.HasIndex(item => item.UserId);
        builder.HasIndex(item => new { item.UserId, item.IsArchived });
        builder.HasIndex(item => new { item.GroupConversationId, item.Status });

        builder.HasOne(item => item.GroupConversation)
            .WithMany(item => item.Members)
            .HasForeignKey(item => item.GroupConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.InvitedByUser)
            .WithMany()
            .HasForeignKey(item => item.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
