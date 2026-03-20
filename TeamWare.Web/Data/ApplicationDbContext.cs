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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.DisplayName).HasMaxLength(100);
            entity.Property(u => u.AvatarUrl).HasMaxLength(500);
            entity.Property(u => u.ThemePreference).HasMaxLength(20).HasDefaultValue("system");
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
    }
}
