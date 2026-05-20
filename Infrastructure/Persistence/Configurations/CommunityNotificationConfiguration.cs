using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class CommunityNotificationConfiguration : IEntityTypeConfiguration<CommunityNotification>
{
    public void Configure(EntityTypeBuilder<CommunityNotification> builder)
    {
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Type).HasMaxLength(32);
        builder.Property(notification => notification.Title).HasMaxLength(160);
        builder.Property(notification => notification.Detail).HasMaxLength(800);
        builder.Property(notification => notification.RelatedGroupConversationId).HasColumnType("char(36)");
        builder.Property(notification => notification.ImageUrl).HasColumnType("longtext");
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAtUtc });
        builder.HasIndex(notification => notification.RelatedPostId);
        builder.HasIndex(notification => notification.RelatedCommentId);
        builder.HasIndex(notification => notification.RelatedReplyId);
        builder.HasIndex(notification => notification.RelatedGroupConversationId);

        builder.HasOne(notification => notification.User)
            .WithMany(user => user.Notifications)
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
