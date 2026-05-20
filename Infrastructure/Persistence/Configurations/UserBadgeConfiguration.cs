using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class UserBadgeConfiguration : IEntityTypeConfiguration<UserBadge>
{
    public void Configure(EntityTypeBuilder<UserBadge> builder)
    {
        builder.HasKey(badge => badge.Id);
        builder.Property(badge => badge.Name).HasMaxLength(120).IsRequired();
        builder.Property(badge => badge.IsArchived).HasDefaultValue(false);
        builder.HasIndex(badge => badge.DepartmentId);
        builder.HasIndex(badge => new { badge.Name, badge.DepartmentId }).IsUnique();

        builder.HasOne(badge => badge.Department)
            .WithMany(department => department.Badges)
            .HasForeignKey(badge => badge.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasData(new UserBadge
        {
            Id = ClubMembershipDefaults.MemberBadgeId,
            Name = ClubMembershipDefaults.MemberBadgeName,
            DepartmentId = null,
            CreatedAtUtc = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
            IsArchived = false
        });
    }
}
