using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(department => department.Id);
        builder.Property(department => department.Name).HasMaxLength(120).IsRequired();
        builder.Property(department => department.Description).HasMaxLength(1000).HasDefaultValue(string.Empty);
        builder.Property(department => department.IsArchived).HasDefaultValue(false);
        builder.HasIndex(department => department.Name).IsUnique();
        builder.HasIndex(department => department.IsArchived);

        builder.HasOne(department => department.CreatedBy)
            .WithMany()
            .HasForeignKey(department => department.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
