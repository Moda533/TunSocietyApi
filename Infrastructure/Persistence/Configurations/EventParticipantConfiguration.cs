using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class EventParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Status).HasMaxLength(32);
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime(6)");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime(6)");

        builder.HasIndex(item => new { item.EventId, item.UserId }).IsUnique();
        builder.HasIndex(item => new { item.EventId, item.Status });
        builder.HasIndex(item => item.UserId);

        builder.HasOne(item => item.Event)
            .WithMany(item => item.Participants)
            .HasForeignKey(item => item.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
