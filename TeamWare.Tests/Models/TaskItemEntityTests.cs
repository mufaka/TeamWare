using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class TaskItemEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly Project _project;

    public TaskItemEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _user = new ApplicationUser
        {
            UserName = "test@test.com",
            Email = "test@test.com",
            DisplayName = "Test User"
        };
        _context.Users.Add(_user);

        _project = new Project { Name = "Test Project" };
        _context.Projects.Add(_project);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateTaskItem()
    {
        var task = new TaskItem
        {
            Title = "Test Task",
            Description = "A test task",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == "Test Task");
        Assert.NotNull(retrieved);
        Assert.Equal("Test Task", retrieved.Title);
        Assert.Equal("A test task", retrieved.Description);
    }

    [Fact]
    public async Task TaskItem_DefaultStatus_IsToDo()
    {
        var task = new TaskItem
        {
            Title = "Default Status Task",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == "Default Status Task");
        Assert.NotNull(retrieved);
        Assert.Equal(TaskItemStatus.ToDo, retrieved.Status);
    }

    [Fact]
    public async Task TaskItem_DefaultPriority_IsMedium()
    {
        var task = new TaskItem
        {
            Title = "Default Priority Task",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == "Default Priority Task");
        Assert.NotNull(retrieved);
        Assert.Equal(TaskItemPriority.Medium, retrieved.Priority);
    }

    [Fact]
    public async Task TaskItem_Title_IsRequired()
    {
        var task = new TaskItem
        {
            Title = null!,
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task TaskItem_Description_IsOptional()
    {
        var task = new TaskItem
        {
            Title = "No Description",
            Description = null,
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == "No Description");
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Description);
    }

    [Fact]
    public async Task TaskItem_DueDate_IsOptional()
    {
        var task = new TaskItem
        {
            Title = "No Due Date",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id,
            DueDate = null
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == "No Due Date");
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.DueDate);
    }

    [Theory]
    [InlineData(TaskItemStatus.ToDo)]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.InReview)]
    [InlineData(TaskItemStatus.Done)]
    public async Task TaskItem_CanHaveAllStatuses(TaskItemStatus status)
    {
        var task = new TaskItem
        {
            Title = $"Status {status}",
            Status = status,
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == $"Status {status}");
        Assert.NotNull(retrieved);
        Assert.Equal(status, retrieved.Status);
    }

    [Theory]
    [InlineData(TaskItemPriority.Low)]
    [InlineData(TaskItemPriority.Medium)]
    [InlineData(TaskItemPriority.High)]
    [InlineData(TaskItemPriority.Critical)]
    public async Task TaskItem_CanHaveAllPriorities(TaskItemPriority priority)
    {
        var task = new TaskItem
        {
            Title = $"Priority {priority}",
            Priority = priority,
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == $"Priority {priority}");
        Assert.NotNull(retrieved);
        Assert.Equal(priority, retrieved.Priority);
    }

    [Fact]
    public async Task TaskItem_GTD_DefaultsToFalse()
    {
        var task = new TaskItem
        {
            Title = "GTD Defaults",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems.FirstOrDefaultAsync(t => t.Title == "GTD Defaults");
        Assert.NotNull(retrieved);
        Assert.False(retrieved.IsNextAction);
        Assert.False(retrieved.IsSomedayMaybe);
    }

    [Fact]
    public async Task TaskItem_CascadeDelete_WhenProjectDeleted()
    {
        var project = new Project { Name = "Delete Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.TaskItems.Add(new TaskItem
        {
            Title = "Cascade Task",
            ProjectId = project.Id,
            CreatedByUserId = _user.Id
        });
        await _context.SaveChangesAsync();

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        var tasks = await _context.TaskItems.Where(t => t.ProjectId == project.Id).ToListAsync();
        Assert.Empty(tasks);
    }

    [Fact]
    public async Task TaskItem_BelongsToProject()
    {
        var task = new TaskItem
        {
            Title = "Project Task",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Title == "Project Task");

        Assert.NotNull(retrieved);
        Assert.Equal("Test Project", retrieved.Project.Name);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
