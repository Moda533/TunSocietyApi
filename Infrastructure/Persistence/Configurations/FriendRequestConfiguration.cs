using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Status).HasMaxLength(32);
        builder.Property(request => request.Note).HasMaxLength(500);
        builder.HasIndex(request => new { request.RequesterUserId, request.RecipientUserId }).IsUnique();
        builder.HasIndex(request => request.CreatedAtUtc);

        builder.HasOne(request => request.RequesterUser)
            .WithMany(user => user.SentFriendRequests)
            .HasForeignKey(request => request.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(request => request.RecipientUser)
            .WithMany(user => user.ReceivedFriendRequests)
            .HasForeignKey(request => request.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
