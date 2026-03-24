using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Prompts;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class TaskBreakdownPromptTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;

    public TaskBreakdownPromptTests()
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
    public async Task TaskBreakdown_ReturnsTwoMessages()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await TaskBreakdownPrompt.task_breakdown(
            principal, _taskService, project.Id, "Implement user authentication");

        var messageList = messages.ToList();
        Assert.Equal(2, messageList.Count);
        Assert.Equal(ChatRole.System, messageList[0].Role);
        Assert.Equal(ChatRole.User, messageList[1].Role);
    }

    [Fact]
    public async Task TaskBreakdown_SystemMessageIncludesGuidelines()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await TaskBreakdownPrompt.task_breakdown(
            principal, _taskService, project.Id, "Build a dashboard");

        var systemMessage = messages.First().Text;
        Assert.Contains("3-7 actionable subtasks", systemMessage);
        Assert.Contains("duplicating", systemMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskBreakdown_IncludesExistingTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Existing Task Alpha", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Existing Task Beta", null, TaskItemPriority.Low, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await TaskBreakdownPrompt.task_breakdown(
            principal, _taskService, project.Id, "Add logging");

        var systemMessage = messages.First().Text;
        Assert.Contains("Existing Task Alpha", systemMessage);
        Assert.Contains("Existing Task Beta", systemMessage);
        Assert.Contains("Existing Tasks in Project (2)", systemMessage);
    }

    [Fact]
    public async Task TaskBreakdown_UserMessageIncludesTaskDescription()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await TaskBreakdownPrompt.task_breakdown(
            principal, _taskService, project.Id, "Implement OAuth2 login flow");

        var userMessage = messages.Last().Text;
        Assert.Contains("Implement OAuth2 login flow", userMessage);
    }

    [Fact]
    public async Task TaskBreakdown_EmptyProject_SaysNoTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await TaskBreakdownPrompt.task_breakdown(
            principal, _taskService, project.Id, "Some feature");

        var systemMessage = messages.First().Text;
        Assert.Contains("currently has no tasks", systemMessage);
    }

    [Fact]
    public async Task TaskBreakdown_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var principal = CreateClaimsPrincipal(stranger.Id);

        var messages = await TaskBreakdownPrompt.task_breakdown(
            principal, _taskService, project.Id, "Some task");

        var content = messages.First().Text;
        Assert.Contains("Error", content);
    }
}
