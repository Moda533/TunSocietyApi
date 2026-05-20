using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class PostReactionConfiguration : IEntityTypeConfiguration<PostReaction>
{
    public void Configure(EntityTypeBuilder<PostReaction> builder)
    {
        builder.HasKey(reaction => reaction.Id);
        builder.Property(reaction => reaction.ReactionType).HasMaxLength(32);
        builder.HasIndex(reaction => new { reaction.PostId, reaction.UserId }).IsUnique();
        builder.HasIndex(reaction => reaction.CreatedAtUtc);

        builder.HasOne(reaction => reaction.Post)
            .WithMany(post => post.Reactions)
            .HasForeignKey(reaction => reaction.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(reaction => reaction.User)
            .WithMany(user => user.PostReactions)
            .HasForeignKey(reaction => reaction.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
