using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class NotificationEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;

    public NotificationEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _user = new ApplicationUser
        {
            UserName = "test@test.com",
            Email = "test@test.com",
            DisplayName = "Test User"
        };
        _context.Users.Add(_user);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateNotification()
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = "You have been assigned a task.",
            Type = NotificationType.TaskAssigned,
            ReferenceId = 1
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("You have been assigned a task.", retrieved.Message);
        Assert.Equal(NotificationType.TaskAssigned, retrieved.Type);
        Assert.False(retrieved.IsRead);
        Assert.Equal(1, retrieved.ReferenceId);
    }

    [Fact]
    public async Task Notification_DefaultIsReadFalse()
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = "Test notification",
            Type = NotificationType.StatusChanged
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.False(retrieved.IsRead);
    }

    [Fact]
    public async Task Notification_HasTimestamp()
    {
        var before = DateTime.UtcNow;

        var notification = new Notification
        {
            UserId = _user.Id,
            Message = "Timestamped notification",
            Type = NotificationType.CommentAdded
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= before);
    }

    [Fact]
    public async Task Notification_NavigationToUser()
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = "Navigation test",
            Type = NotificationType.TaskAssigned
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications
            .Include(n => n.User)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.User);
        Assert.Equal(_user.DisplayName, retrieved.User.DisplayName);
    }

    [Fact]
    public async Task Notification_ReferenceIdIsOptional()
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = "No reference",
            Type = NotificationType.InboxThreshold
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.ReferenceId);
    }

    [Theory]
    [InlineData(NotificationType.TaskAssigned)]
    [InlineData(NotificationType.DeadlineApproaching)]
    [InlineData(NotificationType.StatusChanged)]
    [InlineData(NotificationType.CommentAdded)]
    [InlineData(NotificationType.InboxThreshold)]
    public async Task Notification_CanHaveAllTypes(NotificationType type)
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = $"Type test: {type}",
            Type = type
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(type, retrieved.Type);
    }

    [Fact]
    public async Task Notification_CascadeDeleteWithUser()
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = "Will be cascade deleted",
            Type = NotificationType.TaskAssigned
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        _context.Users.Remove(_user);
        await _context.SaveChangesAsync();

        var notifications = await _context.Notifications.ToListAsync();
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task Notification_MessageMaxLength500()
    {
        var longMessage = new string('A', 500);
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = longMessage,
            Type = NotificationType.TaskAssigned
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(500, retrieved.Message.Length);
    }

    [Fact]
    public async Task Notification_MessageIsRequired()
    {
        var notification = new Notification
        {
            UserId = _user.Id,
            Message = string.Empty,
            Type = NotificationType.TaskAssigned
        };

        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(notification);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            notification, validationContext, validationResults, true);

        Assert.False(isValid);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
