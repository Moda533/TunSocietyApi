using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Models;

namespace TunSociety.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ModerationResult> ModerationResults => Set<ModerationResult>();
    public DbSet<Warning> Warnings => Set<Warning>();
    public DbSet<Freeze> Freezes => Set<Freeze>();
    public DbSet<Appeal> Appeals => Set<Appeal>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<PostReaction> PostReactions => Set<PostReaction>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<CommunityNotification> Notifications => Set<CommunityNotification>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<DirectMessageReadCursor> DirectMessageReadCursors => Set<DirectMessageReadCursor>();
    public DbSet<CommunityEvent> Events => Set<CommunityEvent>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventComment> EventComments => Set<EventComment>();
    public DbSet<EventEvaluation> EventEvaluations => Set<EventEvaluation>();
    public DbSet<GroupConversation> GroupConversations => Set<GroupConversation>();
    public DbSet<GroupConversationMember> GroupConversationMembers => Set<GroupConversationMember>();
    public DbSet<GroupMessage> GroupMessages => Set<GroupMessage>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
