using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Models;

namespace TeamWare.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

    public DbSet<InboxItem> InboxItems => Set<InboxItem>();

    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<UserReview> UserReviews => Set<UserReview>();

    public DbSet<AdminActivityLog> AdminActivityLogs => Set<AdminActivityLog>();

    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();

    public DbSet<LoungeMessage> LoungeMessages => Set<LoungeMessage>();

    public DbSet<LoungeReaction> LoungeReactions => Set<LoungeReaction>();

    public DbSet<LoungeReadPosition> LoungeReadPositions => Set<LoungeReadPosition>();

    public DbSet<GlobalConfiguration> GlobalConfigurations => Set<GlobalConfiguration>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    public DbSet<AgentConfiguration> AgentConfigurations => Set<AgentConfiguration>();

    public DbSet<AgentRepository> AgentRepositories => Set<AgentRepository>();

    public DbSet<AgentMcpServer> AgentMcpServers => Set<AgentMcpServer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.DisplayName).HasMaxLength(100);
            entity.Property(u => u.AvatarUrl).HasMaxLength(500);
            entity.Property(u => u.ThemePreference).HasMaxLength(20).HasDefaultValue("system");
            entity.Property(u => u.IsAgent).HasDefaultValue(false);
            entity.Property(u => u.AgentDescription).HasMaxLength(2000);
            entity.Property(u => u.IsAgentActive).HasDefaultValue(true);
        });

        builder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(p => p.Status);
        });

        builder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(pm => pm.Id);
            entity.Property(pm => pm.Role).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(pm => pm.Project)
                .WithMany(p => p.Members)
                .HasForeignKey(pm => pm.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pm => pm.User)
                .WithMany()
                .HasForeignKey(pm => pm.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(pm => new { pm.ProjectId, pm.UserId }).IsUnique();
            entity.HasIndex(pm => pm.UserId);
        });

        builder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title).IsRequired().HasMaxLength(300);
            entity.Property(t => t.Description).HasMaxLength(4000);
            entity.Property(t => t.Status).HasMaxLength(20);
            entity.Property(t => t.Priority).HasMaxLength(20);

            entity.HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(t => t.ProjectId);
            entity.HasIndex(t => t.Status);
            entity.HasIndex(t => t.Priority);
            entity.HasIndex(t => t.DueDate);
            entity.HasIndex(t => t.IsNextAction);
            entity.HasIndex(t => t.IsSomedayMaybe);

            // Composite index for common query: tasks per project filtered by status
            entity.HasIndex(t => new { t.ProjectId, t.Status });
        });

        builder.Entity<TaskAssignment>(entity =>
        {
            entity.HasKey(ta => ta.Id);

            entity.HasOne(ta => ta.TaskItem)
                .WithMany(t => t.Assignments)
                .HasForeignKey(ta => ta.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ta => ta.User)
                .WithMany()
                .HasForeignKey(ta => ta.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ta => new { ta.TaskItemId, ta.UserId }).IsUnique();
            entity.HasIndex(ta => ta.UserId);
        });

        builder.Entity<InboxItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Title).IsRequired().HasMaxLength(300);
            entity.Property(i => i.Description).HasMaxLength(4000);
            entity.Property(i => i.Status).HasMaxLength(20);

            entity.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.ConvertedToTask)
                .WithMany()
                .HasForeignKey(i => i.ConvertedToTaskId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(i => i.UserId);
            entity.HasIndex(i => i.Status);

            // Composite index for common query: unprocessed inbox items per user
            entity.HasIndex(i => new { i.UserId, i.Status });
        });

        builder.Entity<ActivityLogEntry>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.ChangeType).HasConversion<string>().HasMaxLength(30);
            entity.Property(a => a.OldValue).HasMaxLength(500);
            entity.Property(a => a.NewValue).HasMaxLength(500);

            entity.HasOne(a => a.TaskItem)
                .WithMany()
                .HasForeignKey(a => a.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Project)
                .WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(a => a.TaskItemId);
            entity.HasIndex(a => a.ProjectId);
            entity.HasIndex(a => a.CreatedAt);

            // Composite index for common query: activity log per project sorted by date
            entity.HasIndex(a => new { a.ProjectId, a.CreatedAt });
        });

        builder.Entity<Comment>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Content).IsRequired().HasMaxLength(5000);

            entity.HasOne(c => c.TaskItem)
                .WithMany(t => t.Comments)
                .HasForeignKey(c => c.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => c.TaskItemId);
            entity.HasIndex(c => c.AuthorId);
            entity.HasIndex(c => c.CreatedAt);
        });

        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Message).IsRequired().HasMaxLength(500);
            entity.Property(n => n.Type).HasConversion<string>().HasMaxLength(30);

            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(n => n.UserId);
            entity.HasIndex(n => n.IsRead);
            entity.HasIndex(n => n.CreatedAt);

            // Composite index for common query: unread notifications per user
            entity.HasIndex(n => new { n.UserId, n.IsRead });
        });

        builder.Entity<UserReview>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Notes).HasMaxLength(2000);

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => r.UserId);
            entity.HasIndex(r => r.CompletedAt);
        });

        builder.Entity<AdminActivityLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action).IsRequired().HasMaxLength(100);
            entity.Property(a => a.Details).HasMaxLength(1000);

            entity.HasOne(a => a.AdminUser)
                .WithMany()
                .HasForeignKey(a => a.AdminUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.TargetUser)
                .WithMany()
                .HasForeignKey(a => a.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.TargetProject)
                .WithMany()
                .HasForeignKey(a => a.TargetProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(a => a.AdminUserId);
            entity.HasIndex(a => a.CreatedAt);

            // Composite index for common query: admin activity log sorted by date
            entity.HasIndex(a => new { a.AdminUserId, a.CreatedAt });
        });

        builder.Entity<ProjectInvitation>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(i => i.Project)
                .WithMany(p => p.Invitations)
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.InvitedUser)
                .WithMany()
                .HasForeignKey(i => i.InvitedUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.InvitedByUser)
                .WithMany()
                .HasForeignKey(i => i.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(i => new { i.ProjectId, i.InvitedUserId, i.Status });
            entity.HasIndex(i => i.InvitedUserId);
            entity.HasIndex(i => i.Status);
        });

        builder.Entity<LoungeMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Content).IsRequired().HasMaxLength(4000);

            entity.HasOne(m => m.Project)
                .WithMany(p => p.LoungeMessages)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(m => m.User)
                .WithMany(u => u.LoungeMessages)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.PinnedByUser)
                .WithMany()
                .HasForeignKey(m => m.PinnedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.CreatedTask)
                .WithMany()
                .HasForeignKey(m => m.CreatedTaskId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(m => new { m.ProjectId, m.CreatedAt })
                .HasDatabaseName("IX_LoungeMessage_ProjectId_CreatedAt");

            entity.HasIndex(m => m.UserId)
                .HasDatabaseName("IX_LoungeMessage_UserId");
        });

        builder.Entity<LoungeReaction>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ReactionType).IsRequired().HasMaxLength(50);

            entity.HasOne(r => r.LoungeMessage)
                .WithMany(m => m.Reactions)
                .HasForeignKey(r => r.LoungeMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.LoungeMessageId, r.UserId, r.ReactionType })
                .IsUnique()
                .HasDatabaseName("IX_LoungeReaction_MessageId_UserId_Type");

            entity.HasIndex(r => r.LoungeMessageId)
                .HasDatabaseName("IX_LoungeReaction_MessageId");
        });

        builder.Entity<LoungeReadPosition>(entity =>
        {
            entity.HasKey(rp => rp.Id);

            entity.HasOne(rp => rp.User)
                .WithMany()
                .HasForeignKey(rp => rp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rp => rp.Project)
                .WithMany()
                .HasForeignKey(rp => rp.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(rp => rp.LastReadMessage)
                .WithMany()
                .HasForeignKey(rp => rp.LastReadMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(rp => new { rp.UserId, rp.ProjectId })
                .IsUnique()
                .HasDatabaseName("IX_LoungeReadPosition_UserId_ProjectId");
        });

        builder.Entity<GlobalConfiguration>(entity =>
        {
            entity.HasKey(gc => gc.Id);
            entity.Property(gc => gc.Key).IsRequired().HasMaxLength(100);
            entity.Property(gc => gc.Value).IsRequired().HasMaxLength(2000);
            entity.Property(gc => gc.Description).HasMaxLength(500);

            entity.HasOne(gc => gc.UpdatedByUser)
                .WithMany()
                .HasForeignKey(gc => gc.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(gc => gc.Key).IsUnique();
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.FileName).IsRequired().HasMaxLength(255);
            entity.Property(a => a.StoredFileName).IsRequired().HasMaxLength(255);
            entity.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(a => a.EntityType).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(a => a.UploadedByUser)
                .WithMany()
                .HasForeignKey(a => a.UploadedByUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(a => new { a.EntityType, a.EntityId });
            entity.HasIndex(a => a.UploadedByUserId);
        });

        builder.Entity<PersonalAccessToken>(entity =>
        {
            entity.HasKey(pat => pat.Id);
            entity.Property(pat => pat.Name).IsRequired().HasMaxLength(100);
            entity.Property(pat => pat.TokenHash).IsRequired().HasMaxLength(128);
            entity.Property(pat => pat.TokenPrefix).IsRequired().HasMaxLength(10);

            entity.HasOne(pat => pat.User)
                .WithMany(u => u.PersonalAccessTokens)
                .HasForeignKey(pat => pat.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(pat => pat.TokenHash).IsUnique();
            entity.HasIndex(pat => pat.UserId);
        });

        builder.Entity<AgentConfiguration>(entity =>
        {
            entity.HasKey(ac => ac.Id);
            entity.Property(ac => ac.UserId).IsRequired();
            entity.Property(ac => ac.Model).HasMaxLength(200);
            entity.Property(ac => ac.SystemPrompt).HasMaxLength(10000);
            entity.Property(ac => ac.RepositoryUrl).HasMaxLength(500);
            entity.Property(ac => ac.RepositoryBranch).HasMaxLength(200);
            entity.Property(ac => ac.EncryptedRepositoryAccessToken).HasMaxLength(2000);

            entity.HasOne(ac => ac.User)
                .WithOne(u => u.AgentConfiguration)
                .HasForeignKey<AgentConfiguration>(ac => ac.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ac => ac.UserId).IsUnique();
        });

        builder.Entity<AgentRepository>(entity =>
        {
            entity.HasKey(ar => ar.Id);
            entity.Property(ar => ar.ProjectName).IsRequired().HasMaxLength(200);
            entity.Property(ar => ar.Url).IsRequired().HasMaxLength(500);
            entity.Property(ar => ar.Branch).HasMaxLength(200);
            entity.Property(ar => ar.EncryptedAccessToken).HasMaxLength(2000);

            entity.HasOne(ar => ar.AgentConfiguration)
                .WithMany(ac => ac.Repositories)
                .HasForeignKey(ar => ar.AgentConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ar => new { ar.AgentConfigurationId, ar.ProjectName }).IsUnique();
        });

        builder.Entity<AgentMcpServer>(entity =>
        {
            entity.HasKey(ms => ms.Id);
            entity.Property(ms => ms.Name).IsRequired().HasMaxLength(200);
            entity.Property(ms => ms.Type).IsRequired().HasMaxLength(20);
            entity.Property(ms => ms.Url).HasMaxLength(500);
            entity.Property(ms => ms.EncryptedAuthHeader).HasMaxLength(2000);
            entity.Property(ms => ms.Command).HasMaxLength(500);
            entity.Property(ms => ms.Args).HasMaxLength(4000);
            entity.Property(ms => ms.EncryptedEnv).HasMaxLength(8000);

            entity.HasOne(ms => ms.AgentConfiguration)
                .WithMany(ac => ac.McpServers)
                .HasForeignKey(ms => ms.AgentConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
