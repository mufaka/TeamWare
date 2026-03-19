using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ProgressServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProgressService _progressService;
    private readonly TaskService _taskService;
    private readonly ProjectService _projectService;
    private readonly ActivityLogService _activityLogService;

    public ProgressServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _activityLogService = new ActivityLogService(_context);
        _taskService = new TaskService(_context, _activityLogService);
        _projectService = new ProjectService(_context);
        _progressService = new ProgressService(_context);
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

    private async Task<TaskItem> CreateTestTask(int projectId, string userId, string title = "Test Task",
        TaskItemPriority priority = TaskItemPriority.Medium, DateTime? dueDate = null)
    {
        var result = await _taskService.CreateTask(projectId, title, null, priority, dueDate, userId);
        return result.Data!;
    }

    // --- GetProjectStatistics ---

    [Fact]
    public async Task GetProjectStatistics_EmptyProject_ReturnsZeros()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var stats = await _progressService.GetProjectStatistics(project.Id);

        Assert.Equal(0, stats.TotalTasks);
        Assert.Equal(0, stats.TaskCountToDo);
        Assert.Equal(0, stats.TaskCountInProgress);
        Assert.Equal(0, stats.TaskCountInReview);
        Assert.Equal(0, stats.TaskCountDone);
        Assert.Equal(0, stats.CompletionPercentage);
    }

    [Fact]
    public async Task GetProjectStatistics_WithTasks_ReturnsCounts()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var task1 = await CreateTestTask(project.Id, owner.Id, "Task 1");
        var task2 = await CreateTestTask(project.Id, owner.Id, "Task 2");
        var task3 = await CreateTestTask(project.Id, owner.Id, "Task 3");
        var task4 = await CreateTestTask(project.Id, owner.Id, "Task 4");

        await _taskService.ChangeStatus(task2.Id, TaskItemStatus.InProgress, owner.Id);
        await _taskService.ChangeStatus(task3.Id, TaskItemStatus.InReview, owner.Id);
        await _taskService.ChangeStatus(task4.Id, TaskItemStatus.Done, owner.Id);

        var stats = await _progressService.GetProjectStatistics(project.Id);

        Assert.Equal(4, stats.TotalTasks);
        Assert.Equal(1, stats.TaskCountToDo);
        Assert.Equal(1, stats.TaskCountInProgress);
        Assert.Equal(1, stats.TaskCountInReview);
        Assert.Equal(1, stats.TaskCountDone);
        Assert.Equal(25.0, stats.CompletionPercentage);
    }

    [Fact]
    public async Task GetProjectStatistics_AllDone_Returns100Percent()
    {
        var (project, owner) = await CreateProjectWithOwner();

        var task1 = await CreateTestTask(project.Id, owner.Id, "Task 1");
        var task2 = await CreateTestTask(project.Id, owner.Id, "Task 2");

        await _taskService.ChangeStatus(task1.Id, TaskItemStatus.Done, owner.Id);
        await _taskService.ChangeStatus(task2.Id, TaskItemStatus.Done, owner.Id);

        var stats = await _progressService.GetProjectStatistics(project.Id);

        Assert.Equal(100.0, stats.CompletionPercentage);
    }

    // --- GetOverdueTasks ---

    [Fact]
    public async Task GetOverdueTasks_NoOverdue_ReturnsEmpty()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "No due date");
        await CreateTestTask(project.Id, owner.Id, "Future due", dueDate: DateTime.UtcNow.AddDays(5));

        var overdue = await _progressService.GetOverdueTasks(project.Id);

        Assert.Empty(overdue);
    }

    [Fact]
    public async Task GetOverdueTasks_WithOverdue_ReturnsThem()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "Overdue task", dueDate: DateTime.UtcNow.AddDays(-2));
        await CreateTestTask(project.Id, owner.Id, "Future task", dueDate: DateTime.UtcNow.AddDays(5));

        var overdue = await _progressService.GetOverdueTasks(project.Id);

        Assert.Single(overdue);
        Assert.Equal("Overdue task", overdue[0].Title);
    }

    [Fact]
    public async Task GetOverdueTasks_DoneTasksNotIncluded()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id, "Done overdue", dueDate: DateTime.UtcNow.AddDays(-2));
        await _taskService.ChangeStatus(task.Id, TaskItemStatus.Done, owner.Id);

        var overdue = await _progressService.GetOverdueTasks(project.Id);

        Assert.Empty(overdue);
    }

    [Fact]
    public async Task GetOverdueTasks_OrderedByDueDate()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "Less overdue", dueDate: DateTime.UtcNow.AddDays(-1));
        await CreateTestTask(project.Id, owner.Id, "More overdue", dueDate: DateTime.UtcNow.AddDays(-5));

        var overdue = await _progressService.GetOverdueTasks(project.Id);

        Assert.Equal(2, overdue.Count);
        Assert.Equal("More overdue", overdue[0].Title);
        Assert.Equal("Less overdue", overdue[1].Title);
    }

    // --- GetUpcomingDeadlines ---

    [Fact]
    public async Task GetUpcomingDeadlines_ReturnsTasksWithinRange()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "Due in 3 days", dueDate: DateTime.UtcNow.AddDays(3));
        await CreateTestTask(project.Id, owner.Id, "Due in 10 days", dueDate: DateTime.UtcNow.AddDays(10));
        await CreateTestTask(project.Id, owner.Id, "No due date");

        var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, days: 7);

        Assert.Single(upcoming);
        Assert.Equal("Due in 3 days", upcoming[0].Title);
    }

    [Fact]
    public async Task GetUpcomingDeadlines_ExcludesDoneTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var task = await CreateTestTask(project.Id, owner.Id, "Due soon done", dueDate: DateTime.UtcNow.AddDays(3));
        await _taskService.ChangeStatus(task.Id, TaskItemStatus.Done, owner.Id);

        var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, days: 7);

        Assert.Empty(upcoming);
    }

    [Fact]
    public async Task GetUpcomingDeadlines_ExcludesOverdueTasks()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "Overdue", dueDate: DateTime.UtcNow.AddDays(-2));
        await CreateTestTask(project.Id, owner.Id, "Upcoming", dueDate: DateTime.UtcNow.AddDays(2));

        var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, days: 7);

        Assert.Single(upcoming);
        Assert.Equal("Upcoming", upcoming[0].Title);
    }

    [Fact]
    public async Task GetUpcomingDeadlines_OrderedByDueDate()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "Due in 5 days", dueDate: DateTime.UtcNow.AddDays(5));
        await CreateTestTask(project.Id, owner.Id, "Due in 2 days", dueDate: DateTime.UtcNow.AddDays(2));

        var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, days: 7);

        Assert.Equal(2, upcoming.Count);
        Assert.Equal("Due in 2 days", upcoming[0].Title);
        Assert.Equal("Due in 5 days", upcoming[1].Title);
    }

    [Fact]
    public async Task GetUpcomingDeadlines_IncludesDueToday()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await CreateTestTask(project.Id, owner.Id, "Due today", dueDate: DateTime.UtcNow.Date);

        var upcoming = await _progressService.GetUpcomingDeadlines(project.Id, days: 7);

        Assert.Single(upcoming);
    }

    [Fact]
    public async Task GetUpcomingDeadlines_DoesNotReturnOtherProjectTasks()
    {
        var (project1, owner1) = await CreateProjectWithOwner("Project 1");
        var (project2, owner2) = await CreateProjectWithOwner("Project 2");

        await CreateTestTask(project1.Id, owner1.Id, "P1 due", dueDate: DateTime.UtcNow.AddDays(3));
        await CreateTestTask(project2.Id, owner2.Id, "P2 due", dueDate: DateTime.UtcNow.AddDays(3));

        var upcoming = await _progressService.GetUpcomingDeadlines(project1.Id, days: 7);

        Assert.Single(upcoming);
        Assert.Equal("P1 due", upcoming[0].Title);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
