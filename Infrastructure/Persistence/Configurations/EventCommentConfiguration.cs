using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class EventCommentConfiguration : IEntityTypeConfiguration<EventComment>
{
    public void Configure(EntityTypeBuilder<EventComment> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Content).HasMaxLength(1200);
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)");

        builder.HasIndex(item => new { item.EventId, item.CreatedAtUtc });
        builder.HasIndex(item => item.UserId);

        builder.HasOne(item => item.Event)
            .WithMany(item => item.Comments)
            .HasForeignKey(item => item.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
