using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Name).HasMaxLength(160);
        builder.Property(group => group.CoverImageUrl).HasColumnType("longtext");
        builder.Property(group => group.Visibility).HasMaxLength(32);
        builder.HasIndex(group => new { group.OwnerUserId, group.Visibility });

        builder.HasOne(group => group.OwnerUser)
            .WithMany(user => user.OwnedGroups)
            .HasForeignKey(group => group.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
