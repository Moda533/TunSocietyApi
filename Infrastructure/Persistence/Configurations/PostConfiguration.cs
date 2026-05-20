using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.HasKey(post => post.Id);
        builder.Property(post => post.Title).HasMaxLength(120);
        builder.Property(post => post.Content).HasMaxLength(4000);
        builder.Property(post => post.ImageUrl).HasColumnType("longtext");
        builder.Property(post => post.Visibility).HasMaxLength(16);
        builder.HasIndex(post => post.CreatedAtUtc);

        builder.HasOne(post => post.User)
            .WithMany(user => user.Posts)
            .HasForeignKey(post => post.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
