using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Resources;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class DashboardResourceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly NotificationService _notificationService;
    private readonly InboxService _inboxService;
    private readonly ProgressService _progressService;
    private readonly ActivityLogService _activityLogService;

    public DashboardResourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _projectService = new ProjectService(_context);
        _activityLogService = new ActivityLogService(_context);
        _notificationService = new NotificationService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _inboxService = new InboxService(_context, _taskService, _notificationService);
        _progressService = new ProgressService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
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

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner()
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject("Test Project", null, owner.Id);
        return (result.Data!, owner);
    }

    [Fact]
    public async Task GetDashboard_ReturnsValidJson()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await DashboardResource.GetDashboard(
            principal, _taskService, _notificationService, _inboxService, _progressService, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetDashboard_IncludesAssignedTaskCount()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await DashboardResource.GetDashboard(
            principal, _taskService, _notificationService, _inboxService, _progressService, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("assignedTaskCount", out var count));
        Assert.Equal(0, count.GetInt32());
    }

    [Fact]
    public async Task GetDashboard_IncludesUnreadNotificationCount()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await DashboardResource.GetDashboard(
            principal, _taskService, _notificationService, _inboxService, _progressService, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("unreadNotificationCount", out var count));
        Assert.Equal(0, count.GetInt32());
    }

    [Fact]
    public async Task GetDashboard_IncludesUnprocessedInboxCount()
    {
        var owner = CreateUser("user@test.com", "Test User");
        await _inboxService.AddItem("Inbox Item 1", null, owner.Id);
        await _inboxService.AddItem("Inbox Item 2", null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await DashboardResource.GetDashboard(
            principal, _taskService, _notificationService, _inboxService, _progressService, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("unprocessedInboxCount", out var count));
        Assert.Equal(2, count.GetInt32());
    }

    [Fact]
    public async Task GetDashboard_IncludesUpcomingDeadlines()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var dueDate = DateTime.UtcNow.AddDays(3);
        await _taskService.CreateTask(project.Id, "Deadline Task", null, TaskItemPriority.High, dueDate, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await DashboardResource.GetDashboard(
            principal, _taskService, _notificationService, _inboxService, _progressService, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("upcomingDeadlines", out var deadlines));
        Assert.Equal(JsonValueKind.Array, deadlines.ValueKind);
        Assert.True(deadlines.GetArrayLength() >= 1);

        var first = deadlines[0];
        Assert.True(first.TryGetProperty("taskId", out _));
        Assert.True(first.TryGetProperty("title", out _));
        Assert.True(first.TryGetProperty("projectName", out _));
        Assert.True(first.TryGetProperty("dueDate", out _));
    }

    [Fact]
    public async Task GetDashboard_NoData_ReturnsZeroCounts()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await DashboardResource.GetDashboard(
            principal, _taskService, _notificationService, _inboxService, _progressService, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("assignedTaskCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("unreadNotificationCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("unprocessedInboxCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("upcomingDeadlines").GetArrayLength());
    }
}
