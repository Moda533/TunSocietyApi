using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.HasKey(album => album.Id);
        builder.Property(album => album.Name).HasMaxLength(160);
        builder.Property(album => album.CoverImageUrl).HasColumnType("longtext");
        builder.HasIndex(album => new { album.UserId, album.CreatedAtUtc });

        builder.HasOne(album => album.User)
            .WithMany(user => user.Albums)
            .HasForeignKey(album => album.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
