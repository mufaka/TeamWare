using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly NotificationService _notificationService;
    private readonly TaskService _taskService;
    private readonly CommentService _commentService;
    private readonly InboxService _inboxService;
    private readonly ProjectService _projectService;
    private readonly ActivityLogService _activityLogService;

    public NotificationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notificationService = new NotificationService(_context);
        _activityLogService = new ActivityLogService(_context);
        _projectService = new ProjectService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _commentService = new CommentService(_context, _notificationService);
        _inboxService = new InboxService(_context, _taskService, _notificationService);
    }

    private ApplicationUser CreateUser(string email, string displayName)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner(
        string projectName = "Test Project")
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject(projectName, null, owner.Id);
        return (result.Data!, owner);
    }

    private async Task<TaskItem> CreateTestTask(int projectId, string createdByUserId,
        string title = "Test Task")
    {
        var result = await _taskService.CreateTask(projectId, title, null,
            TaskItemPriority.Medium, null, createdByUserId);
        return result.Data!;
    }

    private async Task<ApplicationUser> AddMemberToProject(int projectId)
    {
        var member = CreateUser($"member-{Guid.NewGuid():N}@test.com", "Member");
        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = member.Id,
            Role = ProjectRole.Member
        });
        await _context.SaveChangesAsync();
        return member;
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // --- CreateNotification tests ---

    [Fact]
    public async Task CreateNotification_CreatesNotification()
    {
        var user = CreateUser("user@test.com", "User");

        await _notificationService.CreateNotification(user.Id, "Test message",
            NotificationType.TaskAssigned, 1);

        var notifications = await _context.Notifications.ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(user.Id, notifications[0].UserId);
        Assert.Equal("Test message", notifications[0].Message);
        Assert.Equal(NotificationType.TaskAssigned, notifications[0].Type);
        Assert.Equal(1, notifications[0].ReferenceId);
        Assert.False(notifications[0].IsRead);
    }

    [Fact]
    public async Task CreateNotification_TruncatesLongMessage()
    {
        var user = CreateUser("user@test.com", "User");
        var longMessage = new string('a', 600);

        await _notificationService.CreateNotification(user.Id, longMessage,
            NotificationType.TaskAssigned);

        var notification = await _context.Notifications.FirstAsync();
        Assert.Equal(500, notification.Message.Length);
    }

    [Fact]
    public async Task CreateNotification_WithNullMessage_DoesNotCreate()
    {
        var user = CreateUser("user@test.com", "User");

        await _notificationService.CreateNotification(user.Id, null!,
            NotificationType.TaskAssigned);

        Assert.Empty(await _context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task CreateNotification_WithEmptyUserId_DoesNotCreate()
    {
        await _notificationService.CreateNotification("", "Test message",
            NotificationType.TaskAssigned);

        Assert.Empty(await _context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task CreateNotification_WithoutReferenceId_SetsNull()
    {
        var user = CreateUser("user@test.com", "User");

        await _notificationService.CreateNotification(user.Id, "Test message",
            NotificationType.InboxThreshold);

        var notification = await _context.Notifications.FirstAsync();
        Assert.Null(notification.ReferenceId);
    }

    // --- GetUnreadForUser tests ---

    [Fact]
    public async Task GetUnreadForUser_ReturnsOnlyUnread()
    {
        var user = CreateUser("user@test.com", "User");

        await _notificationService.CreateNotification(user.Id, "Unread 1",
            NotificationType.TaskAssigned);
        await _notificationService.CreateNotification(user.Id, "Unread 2",
            NotificationType.StatusChanged);

        // Mark one as read
        var firstNotification = await _context.Notifications.FirstAsync();
        firstNotification.IsRead = true;
        await _context.SaveChangesAsync();

        var unread = await _notificationService.GetUnreadForUser(user.Id);
        Assert.Single(unread);
        Assert.Equal("Unread 2", unread[0].Message);
    }

    [Fact]
    public async Task GetUnreadForUser_ReturnsOnlyUsersNotifications()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");

        await _notificationService.CreateNotification(user1.Id, "For user 1",
            NotificationType.TaskAssigned);
        await _notificationService.CreateNotification(user2.Id, "For user 2",
            NotificationType.TaskAssigned);

        var unread = await _notificationService.GetUnreadForUser(user1.Id);
        Assert.Single(unread);
        Assert.Equal("For user 1", unread[0].Message);
    }

    [Fact]
    public async Task GetUnreadForUser_OrderedByCreatedAtDescending()
    {
        var user = CreateUser("user@test.com", "User");

        _context.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Message = "Older",
            Type = NotificationType.TaskAssigned,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        });
        _context.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Message = "Newer",
            Type = NotificationType.StatusChanged,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await _context.SaveChangesAsync();

        var unread = await _notificationService.GetUnreadForUser(user.Id);
        Assert.Equal(2, unread.Count);
        Assert.Equal("Newer", unread[0].Message);
        Assert.Equal("Older", unread[1].Message);
    }

    // --- MarkAsRead tests ---

    [Fact]
    public async Task MarkAsRead_Success()
    {
        var user = CreateUser("user@test.com", "User");
        await _notificationService.CreateNotification(user.Id, "Test",
            NotificationType.TaskAssigned);
        var notification = await _context.Notifications.FirstAsync();

        var result = await _notificationService.MarkAsRead(notification.Id, user.Id);

        Assert.True(result.Succeeded);
        var updated = await _context.Notifications.FindAsync(notification.Id);
        Assert.True(updated!.IsRead);
    }

    [Fact]
    public async Task MarkAsRead_NotFound_Fails()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _notificationService.MarkAsRead(999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Notification not found.", result.Errors);
    }

    [Fact]
    public async Task MarkAsRead_OtherUsersNotification_Fails()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");
        await _notificationService.CreateNotification(user1.Id, "Test",
            NotificationType.TaskAssigned);
        var notification = await _context.Notifications.FirstAsync();

        var result = await _notificationService.MarkAsRead(notification.Id, user2.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("You can only manage your own notifications.", result.Errors);
    }

    // --- DismissNotification tests ---

    [Fact]
    public async Task DismissNotification_Success()
    {
        var user = CreateUser("user@test.com", "User");
        await _notificationService.CreateNotification(user.Id, "Test",
            NotificationType.TaskAssigned);
        var notification = await _context.Notifications.FirstAsync();

        var result = await _notificationService.DismissNotification(notification.Id, user.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(await _context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task DismissNotification_NotFound_Fails()
    {
        var user = CreateUser("user@test.com", "User");

        var result = await _notificationService.DismissNotification(999, user.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DismissNotification_OtherUsersNotification_Fails()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");
        await _notificationService.CreateNotification(user1.Id, "Test",
            NotificationType.TaskAssigned);
        var notification = await _context.Notifications.FirstAsync();

        var result = await _notificationService.DismissNotification(notification.Id, user2.Id);

        Assert.False(result.Succeeded);
    }

    // --- GetUnreadCount tests ---

    [Fact]
    public async Task GetUnreadCount_ReturnsCorrectCount()
    {
        var user = CreateUser("user@test.com", "User");

        await _notificationService.CreateNotification(user.Id, "One",
            NotificationType.TaskAssigned);
        await _notificationService.CreateNotification(user.Id, "Two",
            NotificationType.StatusChanged);
        await _notificationService.CreateNotification(user.Id, "Three",
            NotificationType.CommentAdded);

        // Mark one as read
        var first = await _context.Notifications.FirstAsync();
        first.IsRead = true;
        await _context.SaveChangesAsync();

        var count = await _notificationService.GetUnreadCount(user.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetUnreadCount_ZeroForNewUser()
    {
        var user = CreateUser("user@test.com", "User");

        var count = await _notificationService.GetUnreadCount(user.Id);
        Assert.Equal(0, count);
    }

    // --- GetInboxThresholdAlert tests ---

    [Fact]
    public async Task GetInboxThresholdAlert_BelowThreshold_ReturnsFalse()
    {
        var user = CreateUser("user@test.com", "User");

        // Add 5 inbox items (below default threshold of 10)
        for (int i = 0; i < 5; i++)
        {
            await _inboxService.AddItem($"Item {i}", null, user.Id);
        }

        var alert = await _notificationService.GetInboxThresholdAlert(user.Id);
        Assert.False(alert);
    }

    [Fact]
    public async Task GetInboxThresholdAlert_AtThreshold_ReturnsTrue()
    {
        var user = CreateUser("user@test.com", "User");

        for (int i = 0; i < 10; i++)
        {
            await _inboxService.AddItem($"Item {i}", null, user.Id);
        }

        var alert = await _notificationService.GetInboxThresholdAlert(user.Id);
        Assert.True(alert);
    }

    [Fact]
    public async Task GetInboxThresholdAlert_CustomThreshold()
    {
        var user = CreateUser("user@test.com", "User");

        for (int i = 0; i < 3; i++)
        {
            await _inboxService.AddItem($"Item {i}", null, user.Id);
        }

        var alert = await _notificationService.GetInboxThresholdAlert(user.Id, threshold: 3);
        Assert.True(alert);
    }

    // --- Integration: TaskService triggers ---

    [Fact]
    public async Task AssignMembers_CreatesNotificationForAssignedUser()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id);

        var task = await CreateTestTask(project.Id, owner.Id, "Important Task");
        await _taskService.AssignMembers(task.Id, new[] { member.Id }, owner.Id);

        var notifications = await _notificationService.GetUnreadForUser(member.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.TaskAssigned, notifications[0].Type);
        Assert.Contains("Important Task", notifications[0].Message);
        Assert.Equal(task.Id, notifications[0].ReferenceId);
    }

    [Fact]
    public async Task AssignMembers_DoesNotNotifySelfAssignment()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.AssignMembers(task.Id, new[] { owner.Id }, owner.Id);

        var notifications = await _notificationService.GetUnreadForUser(owner.Id);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task ChangeStatus_NotifiesAssignedUsers()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id);

        var task = await CreateTestTask(project.Id, owner.Id, "Status Task");
        await _taskService.AssignMembers(task.Id, new[] { member.Id }, owner.Id);

        // Clear assignment notifications
        var assignmentNotifications = await _context.Notifications
            .Where(n => n.UserId == member.Id)
            .ToListAsync();
        _context.Notifications.RemoveRange(assignmentNotifications);
        await _context.SaveChangesAsync();

        await _taskService.ChangeStatus(task.Id, TaskItemStatus.InProgress, owner.Id);

        var notifications = await _notificationService.GetUnreadForUser(member.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.StatusChanged, notifications[0].Type);
        Assert.Contains("Status Task", notifications[0].Message);
        Assert.Contains("InProgress", notifications[0].Message);
    }

    [Fact]
    public async Task ChangeStatus_DoesNotNotifyUserWhoChangedStatus()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        await _taskService.AssignMembers(task.Id, new[] { owner.Id }, owner.Id);

        await _taskService.ChangeStatus(task.Id, TaskItemStatus.InProgress, owner.Id);

        // Owner should not receive status change notification (they made the change)
        var notifications = await _context.Notifications
            .Where(n => n.UserId == owner.Id && n.Type == NotificationType.StatusChanged)
            .ToListAsync();
        Assert.Empty(notifications);
    }

    // --- Integration: CommentService triggers ---

    [Fact]
    public async Task AddComment_NotifiesAssignedUsers()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = await AddMemberToProject(project.Id);

        var task = await CreateTestTask(project.Id, owner.Id, "Comment Task");
        await _taskService.AssignMembers(task.Id, new[] { member.Id }, owner.Id);

        // Clear assignment notifications
        var existing = await _context.Notifications.Where(n => n.UserId == member.Id).ToListAsync();
        _context.Notifications.RemoveRange(existing);
        await _context.SaveChangesAsync();

        await _commentService.AddComment(task.Id, "Nice work!", owner.Id);

        var notifications = await _notificationService.GetUnreadForUser(member.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.CommentAdded, notifications[0].Type);
        Assert.Contains("Comment Task", notifications[0].Message);
    }

    [Fact]
    public async Task AddComment_DoesNotNotifyCommentAuthor()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);
        await _taskService.AssignMembers(task.Id, new[] { owner.Id }, owner.Id);

        await _commentService.AddComment(task.Id, "My own comment", owner.Id);

        var notifications = await _context.Notifications
            .Where(n => n.UserId == owner.Id && n.Type == NotificationType.CommentAdded)
            .ToListAsync();
        Assert.Empty(notifications);
    }

    // --- Integration: InboxService threshold trigger ---

    [Fact]
    public async Task AddInboxItem_AtThreshold_CreatesNotification()
    {
        var user = CreateUser("user@test.com", "User");

        // Add items to reach threshold
        for (int i = 0; i < 10; i++)
        {
            await _inboxService.AddItem($"Item {i}", null, user.Id);
        }

        var notifications = await _context.Notifications
            .Where(n => n.UserId == user.Id && n.Type == NotificationType.InboxThreshold)
            .ToListAsync();
        Assert.True(notifications.Count > 0);
    }

    [Fact]
    public async Task AddInboxItem_BelowThreshold_NoNotification()
    {
        var user = CreateUser("user@test.com", "User");

        for (int i = 0; i < 5; i++)
        {
            await _inboxService.AddItem($"Item {i}", null, user.Id);
        }

        var notifications = await _context.Notifications
            .Where(n => n.UserId == user.Id && n.Type == NotificationType.InboxThreshold)
            .ToListAsync();
        Assert.Empty(notifications);
    }
}
