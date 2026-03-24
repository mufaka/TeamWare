using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class ActivityToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly ActivityLogService _activityLogService;
    private readonly ProgressService _progressService;
    private readonly ProjectMemberService _projectMemberService;
    private readonly NotificationService _notificationService;

    public ActivityToolsTests()
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
        _progressService = new ProgressService(_context);
        _projectMemberService = new ProjectMemberService(_context);
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

    // --- get_activity ---

    [Fact]
    public async Task GetActivity_ForProject_ReturnsActivityEntries()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task 1", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ActivityTools.get_activity(principal, _activityLogService,
            _projectMemberService, projectId: project.Id, period: "this_month");

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.True(array.GetArrayLength() >= 1);

        var entry = array[0];
        Assert.True(entry.TryGetProperty("timestamp", out _));
        Assert.True(entry.TryGetProperty("changeType", out _));
        Assert.True(entry.TryGetProperty("taskTitle", out _));
    }

    [Fact]
    public async Task GetActivity_ForUser_ReturnsUserActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "User Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ActivityTools.get_activity(principal, _activityLogService,
            _projectMemberService, projectId: null, period: "this_month");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task GetActivity_NonMember_ReturnsError()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await ActivityTools.get_activity(principal, _activityLogService,
            _projectMemberService, projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("member", error.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetActivity_DefaultPeriod_IsThisWeek()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Recent Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        // No period specified, should default to this_week
        var result = await ActivityTools.get_activity(principal, _activityLogService,
            _projectMemberService, projectId: project.Id, period: null);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetActivity_TodayPeriod_FiltersCorrectly()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Today Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ActivityTools.get_activity(principal, _activityLogService,
            _projectMemberService, projectId: project.Id, period: "today");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }

    // --- get_project_summary ---

    [Fact]
    public async Task GetProjectSummary_ReturnsStatisticsAndCounts()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await _taskService.CreateTask(project.Id, "Task 1", null, TaskItemPriority.Medium, null, owner.Id);
        var task2 = await _taskService.CreateTask(project.Id, "Task 2", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.ChangeStatus(task2.Data!.Id, TaskItemStatus.Done, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ActivityTools.get_project_summary(principal, _progressService,
            _activityLogService, _projectMemberService, project.Id, period: "this_month");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("taskStatistics", out var stats));
        Assert.Equal(2, stats.GetProperty("totalTasks").GetInt32());
        Assert.Equal(1, stats.GetProperty("taskCountDone").GetInt32());
        Assert.True(root.TryGetProperty("completionPercentage", out _));
        Assert.True(root.TryGetProperty("overdueCount", out _));
        Assert.True(root.TryGetProperty("completedInPeriod", out _));
        Assert.True(root.TryGetProperty("createdInPeriod", out _));
    }

    [Fact]
    public async Task GetProjectSummary_NonMember_ReturnsError()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await ActivityTools.get_project_summary(principal, _progressService,
            _activityLogService, _projectMemberService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("member", error.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProjectSummary_EmptyProject_ReturnsZeroCounts()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ActivityTools.get_project_summary(principal, _progressService,
            _activityLogService, _projectMemberService, project.Id);

        using var doc = JsonDocument.Parse(result);
        var stats = doc.RootElement.GetProperty("taskStatistics");
        Assert.Equal(0, stats.GetProperty("totalTasks").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("completedInPeriod").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("createdInPeriod").GetInt32());
    }
}
