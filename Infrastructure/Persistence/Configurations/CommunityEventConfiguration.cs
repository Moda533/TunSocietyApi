using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class CommunityEventConfiguration : IEntityTypeConfiguration<CommunityEvent>
{
    public void Configure(EntityTypeBuilder<CommunityEvent> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Title).HasMaxLength(180);
        builder.Property(item => item.Description).HasMaxLength(4000);
        builder.Property(item => item.Location).HasMaxLength(240);
        builder.Property(item => item.ImageUrl).HasColumnType("longtext");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.StartsAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.ChatConversationId).HasColumnType("char(36)");

        builder.HasIndex(item => item.StartsAtUtc);
        builder.HasIndex(item => item.CreatedByUserId);
        builder.HasIndex(item => item.ChatConversationId);

        builder.HasOne(item => item.CreatedByUser)
            .WithMany()
            .HasForeignKey(item => item.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
