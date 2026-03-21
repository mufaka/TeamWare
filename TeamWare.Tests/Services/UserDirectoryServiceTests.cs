using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class UserDirectoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly UserDirectoryService _service;

    public UserDirectoryServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new UserDirectoryService(_context);
    }

    private ApplicationUser CreateUser(string email, string displayName, string? avatarUrl = null)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            AvatarUrl = avatarUrl
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    private Project CreateProject(string name)
    {
        var project = new Project { Name = name };
        _context.Projects.Add(project);
        _context.SaveChanges();
        return project;
    }

    private void AddProjectMember(int projectId, string userId, ProjectRole role = ProjectRole.Member)
    {
        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role
        });
        _context.SaveChanges();
    }

    private TaskItem CreateTask(int projectId, string userId, string title,
        TaskItemStatus status = TaskItemStatus.ToDo, DateTime? dueDate = null)
    {
        var task = new TaskItem
        {
            Title = title,
            ProjectId = projectId,
            CreatedByUserId = userId,
            Status = status,
            DueDate = dueDate
        };
        _context.TaskItems.Add(task);
        _context.SaveChanges();
        return task;
    }

    private void AssignTask(int taskItemId, string userId)
    {
        _context.TaskAssignments.Add(new TaskAssignment
        {
            TaskItemId = taskItemId,
            UserId = userId
        });
        _context.SaveChanges();
    }

    private void CreateActivityLogEntry(string userId, int projectId, int taskItemId,
        ActivityChangeType changeType, DateTime? createdAt = null)
    {
        _context.ActivityLogEntries.Add(new ActivityLogEntry
        {
            UserId = userId,
            ProjectId = projectId,
            TaskItemId = taskItemId,
            ChangeType = changeType,
            CreatedAt = createdAt ?? DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    // --- SearchUsers ---

    [Fact]
    public async Task SearchUsers_NoSearchTerm_ReturnsAllUsers()
    {
        CreateUser("alice@test.com", "Alice");
        CreateUser("bob@test.com", "Bob");
        CreateUser("charlie@test.com", "Charlie");

        var result = await _service.SearchUsers(null);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.TotalCount);
        Assert.Equal(3, result.Data.Items.Count);
    }

    [Fact]
    public async Task SearchUsers_ByDisplayName_ReturnsMatchingUsers()
    {
        CreateUser("alice@test.com", "Alice Smith");
        CreateUser("bob@test.com", "Bob Jones");

        var result = await _service.SearchUsers("alice");

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.Items);
        Assert.Equal("Alice Smith", result.Data.Items[0].DisplayName);
    }

    [Fact]
    public async Task SearchUsers_ByEmail_ReturnsMatchingUsers()
    {
        CreateUser("alice@test.com", "Alice Smith");
        CreateUser("bob@test.com", "Bob Jones");

        var result = await _service.SearchUsers("bob@test");

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.Items);
        Assert.Equal("Bob Jones", result.Data.Items[0].DisplayName);
    }

    [Fact]
    public async Task SearchUsers_CaseInsensitive()
    {
        CreateUser("alice@test.com", "Alice Smith");

        var result = await _service.SearchUsers("ALICE");

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task SearchUsers_NoMatch_ReturnsEmpty()
    {
        CreateUser("alice@test.com", "Alice Smith");

        var result = await _service.SearchUsers("zzznotfound");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalCount);
    }

    [Fact]
    public async Task SearchUsers_Pagination_ReturnsCorrectPage()
    {
        for (int i = 1; i <= 25; i++)
        {
            CreateUser($"user{i:D2}@test.com", $"User {i:D2}");
        }

        var page1 = await _service.SearchUsers(null, page: 1, pageSize: 10);
        var page2 = await _service.SearchUsers(null, page: 2, pageSize: 10);
        var page3 = await _service.SearchUsers(null, page: 3, pageSize: 10);

        Assert.Equal(25, page1.Data!.TotalCount);
        Assert.Equal(10, page1.Data.Items.Count);
        Assert.Equal(10, page2.Data!.Items.Count);
        Assert.Equal(5, page3.Data!.Items.Count);
        Assert.Equal(3, page3.Data.TotalPages);
    }

    [Fact]
    public async Task SearchUsers_OrderedByDisplayName()
    {
        CreateUser("charlie@test.com", "Charlie");
        CreateUser("alice@test.com", "Alice");
        CreateUser("bob@test.com", "Bob");

        var result = await _service.SearchUsers(null);

        Assert.True(result.Succeeded);
        Assert.Equal("Alice", result.Data!.Items[0].DisplayName);
        Assert.Equal("Bob", result.Data.Items[1].DisplayName);
        Assert.Equal("Charlie", result.Data.Items[2].DisplayName);
    }

    [Fact]
    public async Task SearchUsers_ReturnsCorrectFields()
    {
        var user = CreateUser("alice@test.com", "Alice Smith", "https://example.com/avatar.png");

        var result = await _service.SearchUsers(null);

        Assert.True(result.Succeeded);
        var entry = result.Data!.Items[0];
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal("Alice Smith", entry.DisplayName);
        Assert.Equal("alice@test.com", entry.Email);
        Assert.Equal("https://example.com/avatar.png", entry.AvatarUrl);
    }

    // --- GetUsersSorted ---

    [Fact]
    public async Task GetUsersSorted_ByDisplayNameAscending()
    {
        CreateUser("charlie@test.com", "Charlie");
        CreateUser("alice@test.com", "Alice");
        CreateUser("bob@test.com", "Bob");

        var result = await _service.GetUsersSorted("displayname", ascending: true);

        Assert.True(result.Succeeded);
        Assert.Equal("Alice", result.Data!.Items[0].DisplayName);
        Assert.Equal("Bob", result.Data.Items[1].DisplayName);
        Assert.Equal("Charlie", result.Data.Items[2].DisplayName);
    }

    [Fact]
    public async Task GetUsersSorted_ByDisplayNameDescending()
    {
        CreateUser("charlie@test.com", "Charlie");
        CreateUser("alice@test.com", "Alice");
        CreateUser("bob@test.com", "Bob");

        var result = await _service.GetUsersSorted("displayname", ascending: false);

        Assert.True(result.Succeeded);
        Assert.Equal("Charlie", result.Data!.Items[0].DisplayName);
        Assert.Equal("Bob", result.Data.Items[1].DisplayName);
        Assert.Equal("Alice", result.Data.Items[2].DisplayName);
    }

    [Fact]
    public async Task GetUsersSorted_ByEmailAscending()
    {
        CreateUser("charlie@test.com", "Charlie");
        CreateUser("alice@test.com", "Alice");
        CreateUser("bob@test.com", "Bob");

        var result = await _service.GetUsersSorted("email", ascending: true);

        Assert.True(result.Succeeded);
        Assert.Equal("alice@test.com", result.Data!.Items[0].Email);
        Assert.Equal("bob@test.com", result.Data.Items[1].Email);
        Assert.Equal("charlie@test.com", result.Data.Items[2].Email);
    }

    [Fact]
    public async Task GetUsersSorted_ByEmailDescending()
    {
        CreateUser("charlie@test.com", "Charlie");
        CreateUser("alice@test.com", "Alice");
        CreateUser("bob@test.com", "Bob");

        var result = await _service.GetUsersSorted("email", ascending: false);

        Assert.True(result.Succeeded);
        Assert.Equal("charlie@test.com", result.Data!.Items[0].Email);
        Assert.Equal("bob@test.com", result.Data.Items[1].Email);
        Assert.Equal("alice@test.com", result.Data.Items[2].Email);
    }

    [Fact]
    public async Task GetUsersSorted_UnknownSortField_DefaultsToDisplayName()
    {
        CreateUser("charlie@test.com", "Charlie");
        CreateUser("alice@test.com", "Alice");

        var result = await _service.GetUsersSorted("unknownfield", ascending: true);

        Assert.True(result.Succeeded);
        Assert.Equal("Alice", result.Data!.Items[0].DisplayName);
        Assert.Equal("Charlie", result.Data.Items[1].DisplayName);
    }

    [Fact]
    public async Task GetUsersSorted_Pagination_Works()
    {
        for (int i = 1; i <= 15; i++)
        {
            CreateUser($"user{i:D2}@test.com", $"User {i:D2}");
        }

        var result = await _service.GetUsersSorted("displayname", true, page: 2, pageSize: 10);

        Assert.True(result.Succeeded);
        Assert.Equal(15, result.Data!.TotalCount);
        Assert.Equal(5, result.Data.Items.Count);
        Assert.Equal(2, result.Data.Page);
    }

    // --- GetUserProfile ---

    [Fact]
    public async Task GetUserProfile_ReturnsProfileData()
    {
        var user = CreateUser("alice@test.com", "Alice Smith", "https://avatar.com/alice.png");
        var project = CreateProject("My Project");
        AddProjectMember(project.Id, user.Id, ProjectRole.Owner);

        var result = await _service.GetUserProfile(user.Id);

        Assert.True(result.Succeeded);
        var profile = result.Data!;
        Assert.Equal(user.Id, profile.UserId);
        Assert.Equal("Alice Smith", profile.DisplayName);
        Assert.Equal("alice@test.com", profile.Email);
        Assert.Equal("https://avatar.com/alice.png", profile.AvatarUrl);
    }

    [Fact]
    public async Task GetUserProfile_IncludesProjectMemberships()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project1 = CreateProject("Alpha Project");
        var project2 = CreateProject("Beta Project");
        AddProjectMember(project1.Id, user.Id, ProjectRole.Owner);
        AddProjectMember(project2.Id, user.Id, ProjectRole.Member);

        var result = await _service.GetUserProfile(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.ProjectMemberships.Count);
        Assert.Equal("Alpha Project", result.Data.ProjectMemberships[0].ProjectName);
        Assert.Equal("Owner", result.Data.ProjectMemberships[0].Role);
        Assert.Equal("Beta Project", result.Data.ProjectMemberships[1].ProjectName);
        Assert.Equal("Member", result.Data.ProjectMemberships[1].Role);
    }

    [Fact]
    public async Task GetUserProfile_IncludesTaskStatistics()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");
        AddProjectMember(project.Id, user.Id);

        var task1 = CreateTask(project.Id, user.Id, "Task 1", TaskItemStatus.ToDo,
            DateTime.UtcNow.AddDays(-1)); // Overdue
        var task2 = CreateTask(project.Id, user.Id, "Task 2", TaskItemStatus.Done);
        var task3 = CreateTask(project.Id, user.Id, "Task 3", TaskItemStatus.InProgress);

        AssignTask(task1.Id, user.Id);
        AssignTask(task2.Id, user.Id);
        AssignTask(task3.Id, user.Id);

        var result = await _service.GetUserProfile(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.TaskStatistics.TasksAssigned);
        Assert.Equal(1, result.Data.TaskStatistics.TasksCompleted);
        Assert.Equal(1, result.Data.TaskStatistics.TasksOverdue);
    }

    [Fact]
    public async Task GetUserProfile_IncludesRecentActivity()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");
        AddProjectMember(project.Id, user.Id);
        var task = CreateTask(project.Id, user.Id, "Task 1");

        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.Created);
        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.StatusChanged);

        var result = await _service.GetUserProfile(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.RecentActivity.Count);
    }

    [Fact]
    public async Task GetUserProfile_NonExistentUser_ReturnsFailure()
    {
        var result = await _service.GetUserProfile("nonexistent-id");

        Assert.False(result.Succeeded);
        Assert.Contains("User not found", result.Errors[0]);
    }

    // --- GetUserTaskStatistics ---

    [Fact]
    public async Task GetUserTaskStatistics_CalculatesCorrectly()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");

        var todoTask = CreateTask(project.Id, user.Id, "Todo", TaskItemStatus.ToDo);
        var doneTask = CreateTask(project.Id, user.Id, "Done", TaskItemStatus.Done);
        var overdueTask = CreateTask(project.Id, user.Id, "Overdue", TaskItemStatus.InProgress,
            DateTime.UtcNow.AddDays(-5));
        var notOverdueTask = CreateTask(project.Id, user.Id, "Not Overdue", TaskItemStatus.ToDo,
            DateTime.UtcNow.AddDays(5));

        AssignTask(todoTask.Id, user.Id);
        AssignTask(doneTask.Id, user.Id);
        AssignTask(overdueTask.Id, user.Id);
        AssignTask(notOverdueTask.Id, user.Id);

        var result = await _service.GetUserTaskStatistics(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Data!.TasksAssigned);
        Assert.Equal(1, result.Data.TasksCompleted);
        Assert.Equal(1, result.Data.TasksOverdue);
    }

    [Fact]
    public async Task GetUserTaskStatistics_DoneTaskWithPastDueDate_NotCountedAsOverdue()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");

        var doneTask = CreateTask(project.Id, user.Id, "Done Overdue", TaskItemStatus.Done,
            DateTime.UtcNow.AddDays(-5));
        AssignTask(doneTask.Id, user.Id);

        var result = await _service.GetUserTaskStatistics(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TasksCompleted);
        Assert.Equal(0, result.Data.TasksOverdue);
    }

    [Fact]
    public async Task GetUserTaskStatistics_NoTasks_ReturnsZeros()
    {
        var user = CreateUser("alice@test.com", "Alice");

        var result = await _service.GetUserTaskStatistics(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data!.TasksAssigned);
        Assert.Equal(0, result.Data.TasksCompleted);
        Assert.Equal(0, result.Data.TasksOverdue);
    }

    [Fact]
    public async Task GetUserTaskStatistics_NonExistentUser_ReturnsFailure()
    {
        var result = await _service.GetUserTaskStatistics("nonexistent-id");

        Assert.False(result.Succeeded);
        Assert.Contains("User not found", result.Errors[0]);
    }

    // --- GetUserRecentActivity ---

    [Fact]
    public async Task GetUserRecentActivity_ReturnsLast30Days()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");
        var task = CreateTask(project.Id, user.Id, "Task");

        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.Created,
            DateTime.UtcNow.AddDays(-10));
        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.StatusChanged,
            DateTime.UtcNow.AddDays(-5));
        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.Updated,
            DateTime.UtcNow.AddDays(-40)); // Older than 30 days

        var result = await _service.GetUserRecentActivity(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetUserRecentActivity_OrderedByMostRecentFirst()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");
        var task = CreateTask(project.Id, user.Id, "Task");

        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.Created,
            DateTime.UtcNow.AddDays(-10));
        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.StatusChanged,
            DateTime.UtcNow.AddDays(-1));

        var result = await _service.GetUserRecentActivity(user.Id);

        Assert.True(result.Succeeded);
        Assert.True(result.Data![0].CreatedAt > result.Data[1].CreatedAt);
    }

    [Fact]
    public async Task GetUserRecentActivity_FormatsDescriptionCorrectly()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("My Project");
        var task = CreateTask(project.Id, user.Id, "Important Task");

        CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.Created);

        var result = await _service.GetUserRecentActivity(user.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Contains("Important Task", result.Data[0].Description);
        Assert.Equal("My Project", result.Data[0].ProjectName);
        Assert.Equal(project.Id, result.Data[0].ProjectId);
    }

    [Fact]
    public async Task GetUserRecentActivity_LimitedTo50Entries()
    {
        var user = CreateUser("alice@test.com", "Alice");
        var project = CreateProject("Project");
        var task = CreateTask(project.Id, user.Id, "Task");

        for (int i = 0; i < 60; i++)
        {
            CreateActivityLogEntry(user.Id, project.Id, task.Id, ActivityChangeType.Updated,
                DateTime.UtcNow.AddMinutes(-i));
        }

        var result = await _service.GetUserRecentActivity(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(50, result.Data!.Count);
    }

    [Fact]
    public async Task GetUserRecentActivity_NonExistentUser_ReturnsFailure()
    {
        var result = await _service.GetUserRecentActivity("nonexistent-id");

        Assert.False(result.Succeeded);
        Assert.Contains("User not found", result.Errors[0]);
    }

    [Fact]
    public async Task GetUserRecentActivity_NoActivity_ReturnsEmptyList()
    {
        var user = CreateUser("alice@test.com", "Alice");

        var result = await _service.GetUserRecentActivity(user.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
