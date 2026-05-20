using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(rolePermission => rolePermission.Id);
        builder.Property(rolePermission => rolePermission.Role).HasMaxLength(32).IsRequired();
        builder.Property(rolePermission => rolePermission.Permission).HasMaxLength(128).IsRequired();
        builder.HasIndex(rolePermission => new { rolePermission.Role, rolePermission.Permission }).IsUnique();
    }
}
