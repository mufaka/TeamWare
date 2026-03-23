using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamWare.Web.Data;
using TeamWare.Web.Jobs;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Jobs;

public class TaskDueDateJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly TaskDueDateJob _job;

    public TaskDueDateJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        var logger = loggerFactory.CreateLogger<TaskDueDateJob>();

        _job = new TaskDueDateJob(_context, logger);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // --- Helpers ---

    private ApplicationUser CreateUser(string email = "user@test.com", string displayName = "Test User")
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

    private Project CreateProject(string name = "Test Project")
    {
        var project = new Project { Name = name };
        _context.Projects.Add(project);
        _context.SaveChanges();
        return project;
    }

    private TaskItem CreateTask(int projectId, string userId, DateTime? dueDate = null,
        TaskItemStatus status = TaskItemStatus.ToDo, bool isNextAction = false,
        bool isSomedayMaybe = false, string title = "Test Task")
    {
        var task = new TaskItem
        {
            Title = title,
            ProjectId = projectId,
            CreatedByUserId = userId,
            DueDate = dueDate,
            Status = status,
            IsNextAction = isNextAction,
            IsSomedayMaybe = isSomedayMaybe
        };
        _context.TaskItems.Add(task);
        _context.SaveChanges();
        return task;
    }

    // =============================================
    // TaskDueDateJob Tests
    // =============================================

    [Fact]
    public async Task Execute_PromotesTaskDueWithinTwoDays()
    {
        // Arrange: task due tomorrow with ToDo status
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1));

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.True(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_PromotesTaskDueToday()
    {
        // Arrange: task due today
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow);

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.True(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_PromotesOverdueTask()
    {
        // Arrange: task that was due yesterday
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(-1));

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.True(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_DoesNotPromoteTaskDueFarInFuture()
    {
        // Arrange: task due in 5 days — outside the 2-day window
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(5));

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.False(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_DoesNotPromoteTaskWithNoDueDate()
    {
        // Arrange: task without a due date
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: null);

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.False(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_SkipsTaskAlreadyMarkedNextAction()
    {
        // Arrange: task already marked as next action
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1), isNextAction: true);
        var originalUpdatedAt = task.UpdatedAt;

        // Act
        await _job.Execute();

        // Assert: should not be re-processed (UpdatedAt unchanged)
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.True(updated!.IsNextAction);
        Assert.Equal(originalUpdatedAt, updated.UpdatedAt);
    }

    [Fact]
    public async Task Execute_SkipsInProgressTasks()
    {
        // Arrange: task due tomorrow but InProgress
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1),
            status: TaskItemStatus.InProgress);

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.False(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_SkipsInReviewTasks()
    {
        // Arrange: task due tomorrow but InReview
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1),
            status: TaskItemStatus.InReview);

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.False(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_SkipsDoneTasks()
    {
        // Arrange: task due tomorrow but already Done
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1),
            status: TaskItemStatus.Done);

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.False(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_SkipsSomedayMaybeTasks()
    {
        // Arrange: task due tomorrow but marked Someday/Maybe
        var user = CreateUser();
        var project = CreateProject();
        var task = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1),
            isSomedayMaybe: true);

        // Act
        await _job.Execute();

        // Assert
        var updated = await _context.TaskItems.FindAsync(task.Id);
        Assert.False(updated!.IsNextAction);
    }

    [Fact]
    public async Task Execute_PromotesMultipleEligibleTasks()
    {
        // Arrange: two eligible tasks, one ineligible
        var user = CreateUser();
        var project = CreateProject();
        var eligible1 = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(1), title: "Eligible 1");
        var eligible2 = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow, title: "Eligible 2");
        var ineligible = CreateTask(project.Id, user.Id, dueDate: DateTime.UtcNow.AddDays(5), title: "Ineligible");

        // Act
        await _job.Execute();

        // Assert
        var updated1 = await _context.TaskItems.FindAsync(eligible1.Id);
        var updated2 = await _context.TaskItems.FindAsync(eligible2.Id);
        var updated3 = await _context.TaskItems.FindAsync(ineligible.Id);
        Assert.True(updated1!.IsNextAction);
        Assert.True(updated2!.IsNextAction);
        Assert.False(updated3!.IsNextAction);
    }

    [Fact]
    public async Task Execute_EmptyDatabase_CompletesWithoutError()
    {
        // Act: should not throw
        await _job.Execute();

        // Assert: no tasks existed, nothing to process
        Assert.Equal(0, await _context.TaskItems.CountAsync());
    }
}
