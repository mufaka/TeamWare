using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Prompts;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class StandupPromptTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;

    public StandupPromptTests()
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
    public async Task Standup_ReturnsUserMessage()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var messageList = messages.ToList();
        Assert.Single(messageList);
        Assert.Equal(ChatRole.User, messageList[0].Role);
    }

    [Fact]
    public async Task Standup_IncludesYesterdaySection()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var content = messages.First().Text;
        Assert.Contains("Yesterday", content);
    }

    [Fact]
    public async Task Standup_IncludesTodaySection()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var content = messages.First().Text;
        Assert.Contains("Today", content);
    }

    [Fact]
    public async Task Standup_IncludesBlockersSection()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var content = messages.First().Text;
        Assert.Contains("Blockers", content);
    }

    [Fact]
    public async Task Standup_WithRecentActivity_IncludesActivityEntries()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Recent Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var content = messages.First().Text;
        Assert.Contains("Recent Task", content);
    }

    [Fact]
    public async Task Standup_NoActivity_SaysNoActivity()
    {
        var owner = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var content = messages.First().Text;
        Assert.Contains("No recorded activity", content);
    }

    [Fact]
    public async Task Standup_WithNextActions_IncludesUpcomingTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var taskResult = await _taskService.CreateTask(project.Id, "Next Action Task", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.AssignMembers(taskResult.Data!.Id, new[] { owner.Id }, owner.Id);
        await _taskService.MarkAsNextAction(taskResult.Data!.Id, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await StandupPrompt.standup(principal, _activityLogService, _taskService);

        var content = messages.First().Text;
        Assert.Contains("Next Action Task", content);
    }
}
