using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class ActivityLogEntryEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly Project _project;
    private readonly TaskItem _task;

    public ActivityLogEntryEntityTests()
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
        _context.SaveChanges();

        _project = new Project
        {
            Name = "Test Project",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Projects.Add(_project);
        _context.SaveChanges();

        _task = new TaskItem
        {
            Title = "Test Task",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.TaskItems.Add(_task);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateActivityLogEntry()
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = ActivityChangeType.Created,
            NewValue = "ToDo"
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ActivityLogEntries.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(ActivityChangeType.Created, retrieved.ChangeType);
        Assert.Equal("ToDo", retrieved.NewValue);
        Assert.Null(retrieved.OldValue);
    }

    [Fact]
    public async Task ActivityLogEntry_WithOldAndNewValues()
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = ActivityChangeType.StatusChanged,
            OldValue = "ToDo",
            NewValue = "InProgress"
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ActivityLogEntries.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("ToDo", retrieved.OldValue);
        Assert.Equal("InProgress", retrieved.NewValue);
    }

    [Theory]
    [InlineData(ActivityChangeType.Created)]
    [InlineData(ActivityChangeType.StatusChanged)]
    [InlineData(ActivityChangeType.PriorityChanged)]
    [InlineData(ActivityChangeType.Assigned)]
    [InlineData(ActivityChangeType.Unassigned)]
    [InlineData(ActivityChangeType.MarkedNextAction)]
    [InlineData(ActivityChangeType.ClearedNextAction)]
    [InlineData(ActivityChangeType.MarkedSomedayMaybe)]
    [InlineData(ActivityChangeType.ClearedSomedayMaybe)]
    [InlineData(ActivityChangeType.Updated)]
    [InlineData(ActivityChangeType.Deleted)]
    public async Task ActivityLogEntry_CanHaveAllChangeTypes(ActivityChangeType changeType)
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = changeType
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ActivityLogEntries.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(changeType, retrieved.ChangeType);
    }

    [Fact]
    public async Task ActivityLogEntry_NavigationProperties_Work()
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = ActivityChangeType.Created
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ActivityLogEntries
            .Include(a => a.TaskItem)
            .Include(a => a.Project)
            .Include(a => a.User)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.Equal("Test Task", retrieved.TaskItem.Title);
        Assert.Equal("Test Project", retrieved.Project.Name);
        Assert.Equal("Test User", retrieved.User.DisplayName);
    }

    [Fact]
    public async Task ActivityLogEntry_DefaultCreatedAt_IsSet()
    {
        var before = DateTime.UtcNow;

        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = ActivityChangeType.Created
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ActivityLogEntries.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= before.AddSeconds(-1));
        Assert.True(retrieved.CreatedAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task ActivityLogEntry_CascadeDeleteOnTask()
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = ActivityChangeType.Created
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        _context.TaskItems.Remove(_task);
        await _context.SaveChangesAsync();

        var count = await _context.ActivityLogEntries.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ActivityLogEntry_MultipleEntriesForSameTask()
    {
        var entries = new[]
        {
            new ActivityLogEntry
            {
                TaskItemId = _task.Id,
                ProjectId = _project.Id,
                UserId = _user.Id,
                ChangeType = ActivityChangeType.Created,
                NewValue = "ToDo"
            },
            new ActivityLogEntry
            {
                TaskItemId = _task.Id,
                ProjectId = _project.Id,
                UserId = _user.Id,
                ChangeType = ActivityChangeType.StatusChanged,
                OldValue = "ToDo",
                NewValue = "InProgress"
            },
            new ActivityLogEntry
            {
                TaskItemId = _task.Id,
                ProjectId = _project.Id,
                UserId = _user.Id,
                ChangeType = ActivityChangeType.Assigned,
                NewValue = "Test User"
            }
        };

        _context.ActivityLogEntries.AddRange(entries);
        await _context.SaveChangesAsync();

        var count = await _context.ActivityLogEntries.CountAsync(a => a.TaskItemId == _task.Id);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ActivityLogEntry_OldValueAndNewValue_AreOptional()
    {
        var entry = new ActivityLogEntry
        {
            TaskItemId = _task.Id,
            ProjectId = _project.Id,
            UserId = _user.Id,
            ChangeType = ActivityChangeType.Deleted,
            OldValue = null,
            NewValue = null
        };

        _context.ActivityLogEntries.Add(entry);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ActivityLogEntries.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.OldValue);
        Assert.Null(retrieved.NewValue);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
