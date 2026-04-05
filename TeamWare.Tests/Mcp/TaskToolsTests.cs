using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Tests.Helpers;
using TeamWare.Web.Data;
using TeamWare.Web.Hubs;
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
    private readonly IHubContext<TaskHub> _taskHub;
    private readonly UserManager<ApplicationUser> _userManager;

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
        _taskHub = new StubHubContext<TaskHub>();
        _userManager = TestUserManagerFactory.Create(_context);
    }

    public void Dispose()
    {
        _userManager.Dispose();
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
        Assert.True(task.TryGetProperty("isOverdue", out var isOverdue));
        Assert.False(isOverdue.GetBoolean()); // due in 3 days, not overdue
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

    // --- create_task ---

    [Fact]
    public async Task CreateTask_ReturnsCreatedTask()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "New Task", "A description", "High", "2025-12-31");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("New Task", root.GetProperty("title").GetString());
        Assert.Equal("A description", root.GetProperty("description").GetString());
        Assert.Equal("High", root.GetProperty("priority").GetString());
        Assert.Equal("ToDo", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("projectId", out _));
        Assert.True(root.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task CreateTask_DefaultsPriorityToMedium()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "Default Priority Task");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Medium", doc.RootElement.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task CreateTask_EmptyTitle_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Title is required", error.GetString());
    }

    [Fact]
    public async Task CreateTask_TitleTooLong_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);
        var longTitle = new string('A', 301);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, longTitle);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("300 characters", error.GetString());
    }

    [Fact]
    public async Task CreateTask_DescriptionTooLong_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);
        var longDesc = new string('A', 4001);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "Task", longDesc);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("4000 characters", error.GetString());
    }

    [Fact]
    public async Task CreateTask_InvalidPriority_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "Task", null, "Extreme");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid priority", error.GetString());
    }

    [Fact]
    public async Task CreateTask_InvalidDueDate_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "Task", null, null, "not-a-date");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid due date", error.GetString());
    }

    [Fact]
    public async Task CreateTask_NonMember_ReturnsError()
    {
        var (project, _) = await CreateProjectWithOwner();
        var outsider = CreateUser("outsider-create@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "Task");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    // --- update_task_status ---

    [Fact]
    public async Task UpdateTaskStatus_ChangesStatus()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Status Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, _taskHub, _userManager, createResult.Data!.Id, "InProgress");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("InProgress", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public async Task UpdateTaskStatus_InvalidStatus_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, _taskHub, _userManager, createResult.Data!.Id, "Cancelled");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid status", error.GetString());
    }

    [Fact]
    public async Task UpdateTaskStatus_NonExistent_ReturnsError()
    {
        var user = CreateUser("user-status@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, _taskHub, _userManager, 99999, "Done");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task UpdateTaskStatus_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var outsider = CreateUser("outsider-status@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, _taskHub, _userManager, createResult.Data!.Id, "Done");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    // --- assign_task ---

    [Fact]
    public async Task AssignTask_AssignsUsers()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Assign Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.assign_task(principal, _taskService, createResult.Data!.Id, [owner.Id]);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Contains("1 user(s)", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AssignTask_EmptyUserIds_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.assign_task(principal, _taskService, createResult.Data!.Id, []);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("At least one user ID", error.GetString());
    }

    [Fact]
    public async Task AssignTask_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var outsider = CreateUser("outsider-assign@test.com", "Outsider");
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await TaskTools.assign_task(principal, _taskService, createResult.Data!.Id, [outsider.Id]);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    // --- add_comment ---

    [Fact]
    public async Task AddComment_ReturnsCreatedComment()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Comment Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.add_comment(principal, _commentService, _taskHub, _userManager, createResult.Data!.Id, "Test comment");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("Test comment", root.GetProperty("content").GetString());
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("taskItemId", out _));
        Assert.True(root.TryGetProperty("authorId", out _));
        Assert.True(root.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task AddComment_EmptyContent_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.add_comment(principal, _commentService, _taskHub, _userManager, createResult.Data!.Id, "");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Content is required", error.GetString());
    }

    [Fact]
    public async Task AddComment_ContentTooLong_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);
        var longContent = new string('A', 4001);

        var result = await TaskTools.add_comment(principal, _commentService, _taskHub, _userManager, createResult.Data!.Id, longContent);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("4000 characters", error.GetString());
    }

    [Fact]
    public async Task AddComment_NonExistentTask_ReturnsError()
    {
        var user = CreateUser("user-comment@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await TaskTools.add_comment(principal, _commentService, _taskHub, _userManager, 99999, "Comment");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    // --- my_assignments agent filtering ---

    private static ClaimsPrincipal CreateAgentClaimsPrincipal(string userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("IsAgent", "true")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public async Task MyAssignments_AgentUser_ReturnsOnlyToDoAndInProgress()
    {
        var (project, owner) = await CreateProjectWithOwner();

        // Create tasks in various statuses, all marked as NextAction
        var todoResult = await _taskService.CreateTask(project.Id, "ToDo Task", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.AssignMembers(todoResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(todoResult.Data!.Id, owner.Id);

        var inProgressResult = await _taskService.CreateTask(project.Id, "InProgress Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.AssignMembers(inProgressResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(inProgressResult.Data!.Id, owner.Id);
        await _taskService.ChangeStatus(inProgressResult.Data!.Id, TaskItemStatus.InProgress, owner.Id);

        var inReviewResult = await _taskService.CreateTask(project.Id, "InReview Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.AssignMembers(inReviewResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(inReviewResult.Data!.Id, owner.Id);
        await _taskService.ChangeStatus(inReviewResult.Data!.Id, TaskItemStatus.InReview, owner.Id);

        var agentPrincipal = CreateAgentClaimsPrincipal(owner.Id);

        var result = await TaskTools.my_assignments(agentPrincipal, _taskService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);

        var statuses = new List<string>();
        foreach (var task in array.EnumerateArray())
        {
            statuses.Add(task.GetProperty("status").GetString()!);
        }

        // Agent should only see ToDo and InProgress, not InReview
        Assert.Contains("ToDo", statuses);
        Assert.Contains("InProgress", statuses);
        Assert.DoesNotContain("InReview", statuses);
    }

    [Fact]
    public async Task MyAssignments_HumanUser_ReturnsAllStatuses()
    {
        var (project, owner) = await CreateProjectWithOwner();

        // Create tasks in various statuses, all marked as NextAction
        var todoResult = await _taskService.CreateTask(project.Id, "ToDo Task", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.AssignMembers(todoResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(todoResult.Data!.Id, owner.Id);

        var inReviewResult = await _taskService.CreateTask(project.Id, "InReview Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.AssignMembers(inReviewResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(inReviewResult.Data!.Id, owner.Id);
        await _taskService.ChangeStatus(inReviewResult.Data!.Id, TaskItemStatus.InReview, owner.Id);

        // Human user (no IsAgent claim)
        var humanPrincipal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.my_assignments(humanPrincipal, _taskService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);

        var statuses = new List<string>();
        foreach (var task in array.EnumerateArray())
        {
            statuses.Add(task.GetProperty("status").GetString()!);
        }

        // Human user should see all non-Done statuses including InReview
        Assert.Contains("ToDo", statuses);
        Assert.Contains("InReview", statuses);
    }

    // --- Blocked and Error status support ---

    [Fact]
    public async Task UpdateTaskStatus_ToBlocked_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, _taskHub, _userManager, createResult.Data!.Id, "Blocked");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Blocked", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateTaskStatus_ToError_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, _taskHub, _userManager, createResult.Data!.Id, "Error");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Error", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListTasks_WithBlockedStatusFilter_FiltersCorrectly()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await _taskService.CreateTask(project.Id, "ToDo Task", null, TaskItemPriority.Medium, null, owner.Id);
        var task2 = await _taskService.CreateTask(project.Id, "Blocked Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(task2.Data!.Id, TaskItemStatus.Blocked, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id, status: "Blocked");

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("Blocked", array[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListTasks_WithErrorStatusFilter_FiltersCorrectly()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await _taskService.CreateTask(project.Id, "ToDo Task", null, TaskItemPriority.Medium, null, owner.Id);
        var task2 = await _taskService.CreateTask(project.Id, "Error Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(task2.Data!.Id, TaskItemStatus.Error, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, project.Id, status: "Error");

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("Error", array[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetTask_BlockedStatus_ReturnsBlockedInJson()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Blocked Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(createResult.Data!.Id, TaskItemStatus.Blocked, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.get_task(principal, _taskService, _commentService, createResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Blocked", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetTask_ErrorStatus_ReturnsErrorInJson()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var createResult = await _taskService.CreateTask(project.Id, "Error Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.ChangeStatus(createResult.Data!.Id, TaskItemStatus.Error, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await TaskTools.get_task(principal, _taskService, _commentService, createResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Error", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MyAssignments_AgentUser_ExcludesBlockedAndErrorTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();

        // Create tasks in Blocked and Error statuses, assigned and marked as NextAction
        var blockedResult = await _taskService.CreateTask(project.Id, "Blocked Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.AssignMembers(blockedResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(blockedResult.Data!.Id, owner.Id);
        await _taskService.ChangeStatus(blockedResult.Data!.Id, TaskItemStatus.Blocked, owner.Id);

        var errorResult = await _taskService.CreateTask(project.Id, "Error Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _taskService.AssignMembers(errorResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(errorResult.Data!.Id, owner.Id);
        await _taskService.ChangeStatus(errorResult.Data!.Id, TaskItemStatus.Error, owner.Id);

        // Create a ToDo task that should be returned
        var todoResult = await _taskService.CreateTask(project.Id, "ToDo Task", null, TaskItemPriority.High, null, owner.Id);
        await _taskService.AssignMembers(todoResult.Data!.Id, [owner.Id], owner.Id);
        await _taskService.MarkAsNextAction(todoResult.Data!.Id, owner.Id);

        var agentPrincipal = CreateAgentClaimsPrincipal(owner.Id);

        var result = await TaskTools.my_assignments(agentPrincipal, _taskService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);

        var statuses = new List<string>();
        foreach (var task in array.EnumerateArray())
        {
            statuses.Add(task.GetProperty("status").GetString()!);
        }

        Assert.Contains("ToDo", statuses);
        Assert.DoesNotContain("Blocked", statuses);
        Assert.DoesNotContain("Error", statuses);
    }
}
