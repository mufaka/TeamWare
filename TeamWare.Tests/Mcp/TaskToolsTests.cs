using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class TaskToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly CommentService _commentService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;

    public TaskToolsTests()
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
        _commentService = new CommentService(_context, _notificationService);
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

    // --- list_tasks ---

    [Fact]
    public async Task ListTasks_ReturnsProjectTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task 1", "Desc 1", TaskItemPriority.High, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Task 2", null, TaskItemPriority.Low, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(2, array.GetArrayLength());

        var first = array[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("title", out _));
        Assert.True(first.TryGetProperty("status", out _));
        Assert.True(first.TryGetProperty("priority", out _));
        Assert.True(first.TryGetProperty("assignees", out _));
    }

    [Fact]
    public async Task ListTasks_WithStatusFilter_FiltersCorrectly()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await _taskService.CreateTask(project.Id, "ToDo Task", null, TaskItemPriority.Medium, null, owner.Id);
        var task2 = await _taskService.CreateTask(project.Id, "Done Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(task2.Data!.Id, TaskItemStatus.Done, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id, status: "Done");

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("Done", array[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListTasks_WithPriorityFilter_FiltersCorrectly()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "High Task", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.CreateTask(project.Id, "Low Task", null, TaskItemPriority.Low, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id, priority: "High");

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("High", array[0].GetProperty("priority").GetString());
    }

    [Fact]
    public async Task ListTasks_InvalidStatus_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id, status: "InvalidStatus");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid status", error.GetString());
    }

    [Fact]
    public async Task ListTasks_InvalidPriority_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id, priority: "Extreme");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid priority", error.GetString());
    }

    [Fact]
    public async Task ListTasks_NonMember_ReturnsError()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ListTasks_EnumsSerializedAsStrings()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Critical, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id);

        using var doc = JsonDocument.Parse(result);
        var task = doc.RootElement[0];
        Assert.Equal("ToDo", task.GetProperty("status").GetString());
        Assert.Equal("Critical", task.GetProperty("priority").GetString());
    }

    // --- get_task ---

    [Fact]
    public async Task GetTask_ReturnsFullTaskDetail()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Detailed Task", "Full description",
            TaskItemPriority.High, DateTime.UtcNow.AddDays(7), owner.Id);
        var taskId = createResult.Data!.Id;
        await _commentService.AddComment(taskId, "A comment", owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.get_task(principal, _taskService, _commentService, taskId);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("Detailed Task", root.GetProperty("title").GetString());
        Assert.Equal("Full description", root.GetProperty("description").GetString());
        Assert.Equal("High", root.GetProperty("priority").GetString());
        Assert.Equal("ToDo", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("isNextAction", out _));
        Assert.True(root.TryGetProperty("isSomedayMaybe", out _));
        Assert.True(root.TryGetProperty("projectId", out _));
        Assert.True(root.TryGetProperty("assignees", out _));

        var comments = root.GetProperty("comments");
        Assert.Equal(JsonValueKind.Array, comments.ValueKind);
        Assert.Equal(1, comments.GetArrayLength());
        Assert.Equal("A comment", comments[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetTask_NonExistent_ReturnsError()
    {
        var user = CreateUser("user@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await TaskTools.get_task(principal, _taskService, _commentService, 99999);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task GetTask_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await TaskTools.get_task(principal, _taskService, _commentService, createResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    // --- my_assignments ---

    [Fact]
    public async Task MyAssignments_ReturnsNextActionTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Assigned Task", null,
            TaskItemPriority.High, DateTime.UtcNow.AddDays(3), owner.Id);
        await _taskService.AssignMembers(createResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(createResult.Data!.Id, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.my_assignments(principal, _taskService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.True(array.GetArrayLength() >= 1);

        var task = array[0];
        Assert.True(task.TryGetProperty("id", out _));
        Assert.True(task.TryGetProperty("title", out _));
        Assert.True(task.TryGetProperty("projectId", out _));
        Assert.True(task.TryGetProperty("status", out _));
        Assert.True(task.TryGetProperty("priority", out _));
        Assert.True(task.TryGetProperty("isNextAction", out _));
    }

    [Fact]
    public async Task MyAssignments_NoAssignments_ReturnsEmptyArray()
    {
        var user = CreateUser("unassigned@test.com", "Unassigned User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await TaskTools.my_assignments(principal, _taskService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }
}
