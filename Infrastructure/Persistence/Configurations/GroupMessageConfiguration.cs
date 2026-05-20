using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class GroupMessageConfiguration : IEntityTypeConfiguration<GroupMessage>
{
    public void Configure(EntityTypeBuilder<GroupMessage> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Content).HasMaxLength(2000);
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)");

        builder.HasIndex(item => new { item.GroupConversationId, item.CreatedAtUtc });
        builder.HasIndex(item => item.SenderUserId);

        builder.HasOne(item => item.GroupConversation)
            .WithMany(item => item.Messages)
            .HasForeignKey(item => item.GroupConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.SenderUser)
            .WithMany()
            .HasForeignKey(item => item.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
