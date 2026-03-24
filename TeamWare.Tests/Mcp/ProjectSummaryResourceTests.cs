using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Resources;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class ProjectSummaryResourceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly ProgressService _progressService;
    private readonly TaskService _taskService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;

    public ProjectSummaryResourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _projectService = new ProjectService(_context);
        _progressService = new ProgressService(_context);
        _activityLogService = new ActivityLogService(_context);
        _notificationService = new NotificationService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
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
        var result = await _projectService.CreateProject("Test Project", "Test description", owner.Id);
        return (result.Data!, owner);
    }

    [Fact]
    public async Task GetProjectSummary_ReturnsValidJson()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ProjectSummaryResource.GetProjectSummary(
            principal, _projectService, _progressService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetProjectSummary_IncludesProjectName()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ProjectSummaryResource.GetProjectSummary(
            principal, _projectService, _progressService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Test Project", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetProjectSummary_IncludesStatus()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ProjectSummaryResource.GetProjectSummary(
            principal, _projectService, _progressService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("status", out var status));
        Assert.False(string.IsNullOrEmpty(status.GetString()));
    }

    [Fact]
    public async Task GetProjectSummary_IncludesMemberCount()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ProjectSummaryResource.GetProjectSummary(
            principal, _projectService, _progressService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("memberCount", out var count));
        Assert.True(count.GetInt32() >= 1);
    }

    [Fact]
    public async Task GetProjectSummary_IncludesTaskStats()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task 1", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Task 2", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ProjectSummaryResource.GetProjectSummary(
            principal, _projectService, _progressService, project.Id);

        using var doc = JsonDocument.Parse(result);
        var taskStats = doc.RootElement.GetProperty("taskStats");
        Assert.Equal(2, taskStats.GetProperty("total").GetInt32());
        Assert.True(taskStats.TryGetProperty("toDo", out _));
        Assert.True(taskStats.TryGetProperty("inProgress", out _));
        Assert.True(taskStats.TryGetProperty("inReview", out _));
        Assert.True(taskStats.TryGetProperty("done", out _));
    }

    [Fact]
    public async Task GetProjectSummary_IncludesCompletionPercentage()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await _taskService.CreateTask(project.Id, "Done Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(task1.Data!.Id, TaskItemStatus.Done, owner.Id);
        await _taskService.CreateTask(project.Id, "Open Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await ProjectSummaryResource.GetProjectSummary(
            principal, _projectService, _progressService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("completionPct", out var pct));
        Assert.Equal(50.0, pct.GetDouble());
    }

    [Fact]
    public async Task GetProjectSummary_NonMember_ThrowsMcpException()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var principal = CreateClaimsPrincipal(stranger.Id);

        await Assert.ThrowsAsync<McpException>(() =>
            ProjectSummaryResource.GetProjectSummary(principal, _projectService, _progressService, project.Id));
    }

    [Fact]
    public async Task GetProjectSummary_NonExistentProject_ThrowsMcpException()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var principal = CreateClaimsPrincipal(owner.Id);

        await Assert.ThrowsAsync<McpException>(() =>
            ProjectSummaryResource.GetProjectSummary(principal, _projectService, _progressService, 9999));
    }
}
