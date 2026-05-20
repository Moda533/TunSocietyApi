using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data.Configurations;

public class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        builder.HasKey(comment => comment.Id);
        builder.Property(comment => comment.Content).HasMaxLength(1000);
        builder.Property(comment => comment.MentionedUserIdsJson).HasMaxLength(1000);
        builder.HasIndex(comment => comment.CreatedAtUtc);
        builder.HasIndex(comment => comment.ParentCommentId);

        builder.HasOne(comment => comment.Post)
            .WithMany(post => post.Comments)
            .HasForeignKey(comment => comment.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(comment => comment.ParentComment)
            .WithMany(comment => comment.Replies)
            .HasForeignKey(comment => comment.ParentCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(comment => comment.User)
            .WithMany(user => user.PostComments)
            .HasForeignKey(comment => comment.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
