using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.HasKey(photo => photo.Id);
        builder.Property(photo => photo.MediaUrl).HasColumnType("longtext");
        builder.Property(photo => photo.MediaType).HasMaxLength(16);
        builder.Property(photo => photo.ContentType).HasMaxLength(128);
        builder.Property(photo => photo.OriginalFileName).HasMaxLength(255);
        builder.HasIndex(photo => new { photo.UserId, photo.CreatedAtUtc });
        builder.HasIndex(photo => photo.AlbumId);

        builder.HasOne(photo => photo.User)
            .WithMany(user => user.Photos)
            .HasForeignKey(photo => photo.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(photo => photo.Album)
            .WithMany(album => album.Photos)
            .HasForeignKey(photo => photo.AlbumId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
