using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class DirectMessageConfiguration : IEntityTypeConfiguration<DirectMessage>
{
    public void Configure(EntityTypeBuilder<DirectMessage> builder)
    {
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Content).HasMaxLength(2000);
        builder.HasIndex(message => new { message.SenderUserId, message.RecipientUserId, message.CreatedAtUtc });
        builder.HasIndex(message => new { message.RecipientUserId, message.IsRead, message.CreatedAtUtc });

        builder.HasOne(message => message.SenderUser)
            .WithMany(user => user.SentDirectMessages)
            .HasForeignKey(message => message.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(message => message.RecipientUser)
            .WithMany(user => user.ReceivedDirectMessages)
            .HasForeignKey(message => message.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
