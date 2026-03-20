using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ActivityLogServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ActivityLogService _activityLogService;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly NotificationService _notificationService;

    public ActivityLogServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _activityLogService = new ActivityLogService(_context);
        _notificationService = new NotificationService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _projectService = new ProjectService(_context);
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner(string projectName = "Test Project")
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject(projectName, null, owner.Id);
        return (result.Data!, owner);
    }

    private async Task<TaskItem> CreateTestTask(int projectId, string userId, string title = "Test Task")
    {
        var result = await _taskService.CreateTask(projectId, title, null, TaskItemPriority.Medium, null, userId);
        return result.Data!;
    }

    // --- LogChange ---

    [Fact]
    public async Task LogChange_CreatesEntry()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _activityLogService.LogChange(task.Id, project.Id, owner.Id,
            ActivityChangeType.StatusChanged, "ToDo", "InProgress");

        var entries = await _context.ActivityLogEntries.ToListAsync();
        // 1 from CreateTask + 1 explicit = 2
        Assert.Equal(2, entries.Count);
        var entry = entries.Last();
        Assert.Equal(task.Id, entry.TaskItemId);
        Assert.Equal(project.Id, entry.ProjectId);
        Assert.Equal(owner.Id, entry.UserId);
        Assert.Equal(ActivityChangeType.StatusChanged, entry.ChangeType);
        Assert.Equal("ToDo", entry.OldValue);
        Assert.Equal("InProgress", entry.NewValue);
    }

    [Fact]
    public async Task LogChange_WithNullValues_CreatesEntry()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _activityLogService.LogChange(task.Id, project.Id, owner.Id,
            ActivityChangeType.MarkedNextAction);

        var entries = await _context.ActivityLogEntries.ToListAsync();
        var entry = entries.Last();
        Assert.Equal(ActivityChangeType.MarkedNextAction, entry.ChangeType);
        Assert.Null(entry.OldValue);
        Assert.Null(entry.NewValue);
    }

    // --- GetActivityForProject ---

    [Fact]
    public async Task GetActivityForProject_ReturnsEntriesOrderedByDate()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _activityLogService.LogChange(task.Id, project.Id, owner.Id,
            ActivityChangeType.StatusChanged, "ToDo", "InProgress");
        await _activityLogService.LogChange(task.Id, project.Id, owner.Id,
            ActivityChangeType.Assigned, newValue: "Owner");

        var entries = await _activityLogService.GetActivityForProject(project.Id);

        Assert.True(entries.Count >= 3); // Created + StatusChanged + Assigned
        Assert.True(entries[0].CreatedAt >= entries[1].CreatedAt);
    }

    [Fact]
    public async Task GetActivityForProject_IncludesUserAndTaskData()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var entries = await _activityLogService.GetActivityForProject(project.Id);

        Assert.NotEmpty(entries);
        Assert.NotNull(entries[0].User);
        Assert.NotNull(entries[0].TaskItem);
    }

    [Fact]
    public async Task GetActivityForProject_RespectsCountLimit()
    {
        var (project, owner) = await CreateProjectWithOwner();

        for (int i = 0; i < 5; i++)
        {
            await CreateTestTask(project.Id, owner.Id, $"Task {i}");
        }

        var entries = await _activityLogService.GetActivityForProject(project.Id, count: 3);

        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task GetActivityForProject_DoesNotReturnOtherProjectEntries()
    {
        var (project1, owner1) = await CreateProjectWithOwner("Project 1");
        var (project2, owner2) = await CreateProjectWithOwner("Project 2");

        await CreateTestTask(project1.Id, owner1.Id, "Task in P1");
        await CreateTestTask(project2.Id, owner2.Id, "Task in P2");

        var entries = await _activityLogService.GetActivityForProject(project1.Id);

        Assert.All(entries, e => Assert.Equal(project1.Id, e.ProjectId));
    }

    // --- GetActivityForTask ---

    [Fact]
    public async Task GetActivityForTask_ReturnsEntriesForSpecificTask()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task1 = await CreateTestTask(project.Id, owner.Id, "Task 1");
        var task2 = await CreateTestTask(project.Id, owner.Id, "Task 2");

        await _activityLogService.LogChange(task1.Id, project.Id, owner.Id,
            ActivityChangeType.StatusChanged, "ToDo", "InProgress");

        var entries = await _activityLogService.GetActivityForTask(task1.Id);

        Assert.All(entries, e => Assert.Equal(task1.Id, e.TaskItemId));
        Assert.True(entries.Count >= 2); // Created + StatusChanged
    }

    [Fact]
    public async Task GetActivityForTask_IncludesUserData()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        var entries = await _activityLogService.GetActivityForTask(task.Id);

        Assert.NotEmpty(entries);
        Assert.NotNull(entries[0].User);
    }

    [Fact]
    public async Task GetActivityForTask_OrderedByDateDescending()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _activityLogService.LogChange(task.Id, project.Id, owner.Id,
            ActivityChangeType.StatusChanged, "ToDo", "InProgress");
        await _activityLogService.LogChange(task.Id, project.Id, owner.Id,
            ActivityChangeType.Assigned, newValue: "Owner");

        var entries = await _activityLogService.GetActivityForTask(task.Id);

        for (int i = 0; i < entries.Count - 1; i++)
        {
            Assert.True(entries[i].CreatedAt >= entries[i + 1].CreatedAt);
        }
    }

    // --- Integration with TaskService ---

    [Fact]
    public async Task TaskService_CreateTask_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();

        await _taskService.CreateTask(project.Id, "New Task", null, TaskItemPriority.High, null, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.Created)
            .ToListAsync();
        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task TaskService_ChangeStatus_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.ChangeStatus(task.Id, TaskItemStatus.InProgress, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.StatusChanged && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
        Assert.Equal("ToDo", entries[0].OldValue);
        Assert.Equal("InProgress", entries[0].NewValue);
    }

    [Fact]
    public async Task TaskService_UpdateTask_LogsPriorityChange()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.UpdateTask(task.Id, "Updated Title", null, TaskItemPriority.Critical, null, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.PriorityChanged && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Medium", entries[0].OldValue);
        Assert.Equal("Critical", entries[0].NewValue);
    }

    [Fact]
    public async Task TaskService_UpdateTask_LogsUpdateWhenPriorityUnchanged()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.UpdateTask(task.Id, "Updated Title", "New desc", TaskItemPriority.Medium, null, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.Updated && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
    }

    [Fact]
    public async Task TaskService_MarkAsNextAction_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.MarkAsNextAction(task.Id, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.MarkedNextAction && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
    }

    [Fact]
    public async Task TaskService_ClearNextAction_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.MarkAsNextAction(task.Id, owner.Id);
        await _taskService.ClearNextAction(task.Id, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.ClearedNextAction && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
    }

    [Fact]
    public async Task TaskService_MarkAsSomedayMaybe_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.MarkAsSomedayMaybe(task.Id, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.MarkedSomedayMaybe && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
    }

    [Fact]
    public async Task TaskService_ClearSomedayMaybe_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.MarkAsSomedayMaybe(task.Id, owner.Id);
        await _taskService.ClearSomedayMaybe(task.Id, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.ClearedSomedayMaybe && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
    }

    [Fact]
    public async Task TaskService_AssignMembers_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.AssignMembers(task.Id, new[] { owner.Id }, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.Assigned && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Owner", entries[0].NewValue);
    }

    [Fact]
    public async Task TaskService_UnassignMembers_LogsActivity()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id);

        await _taskService.AssignMembers(task.Id, new[] { owner.Id }, owner.Id);
        await _taskService.UnassignMembers(task.Id, new[] { owner.Id }, owner.Id);

        var entries = await _context.ActivityLogEntries
            .Where(a => a.ChangeType == ActivityChangeType.Unassigned && a.TaskItemId == task.Id)
            .ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Owner", entries[0].OldValue);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
