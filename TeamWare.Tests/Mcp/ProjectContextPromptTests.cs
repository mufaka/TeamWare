using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Prompts;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class ProjectContextPromptTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly ProjectMemberService _projectMemberService;
    private readonly ProgressService _progressService;
    private readonly ActivityLogService _activityLogService;
    private readonly TaskService _taskService;
    private readonly NotificationService _notificationService;

    public ProjectContextPromptTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _projectService = new ProjectService(_context);
        _projectMemberService = new ProjectMemberService(_context);
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
        var result = await _projectService.CreateProject("Test Project", "A test project description", owner.Id);
        return (result.Data!, owner);
    }

    [Fact]
    public async Task ProjectContext_ReturnsSystemMessage()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var messageList = messages.ToList();
        Assert.Single(messageList);
        Assert.Equal(ChatRole.System, messageList[0].Role);
    }

    [Fact]
    public async Task ProjectContext_IncludesProjectName()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var content = messages.First().Text;
        Assert.Contains("Test Project", content);
    }

    [Fact]
    public async Task ProjectContext_IncludesDescription()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var content = messages.First().Text;
        Assert.Contains("A test project description", content);
    }

    [Fact]
    public async Task ProjectContext_IncludesMemberList()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var member = CreateUser("member@test.com", "Team Member");
        await _projectMemberService.InviteMember(project.Id, member.Id, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var content = messages.First().Text;
        Assert.Contains("Members", content);
        Assert.Contains("Owner", content);
    }

    [Fact]
    public async Task ProjectContext_IncludesTaskStatistics()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task 1", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Task 2", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var content = messages.First().Text;
        Assert.Contains("Task Statistics", content);
        Assert.Contains("Total: 2", content);
    }

    [Fact]
    public async Task ProjectContext_IncludesRecentActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Activity Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var content = messages.First().Text;
        Assert.Contains("Recent Activity", content);
    }

    [Fact]
    public async Task ProjectContext_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var principal = CreateClaimsPrincipal(stranger.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, project.Id);

        var content = messages.First().Text;
        Assert.Contains("Error", content);
    }

    [Fact]
    public async Task ProjectContext_NonExistentProject_ReturnsError()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var principal = CreateClaimsPrincipal(owner.Id);

        var messages = await ProjectContextPrompt.project_context(
            principal, _projectService, _projectMemberService, _progressService, _activityLogService, 9999);

        var content = messages.First().Text;
        Assert.Contains("Error", content);
    }
}
