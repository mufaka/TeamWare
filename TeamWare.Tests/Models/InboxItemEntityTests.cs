using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class InboxItemEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;

    public InboxItemEntityTests()
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
    }

    [Fact]
    public async Task CanCreateInboxItem()
    {
        var item = new InboxItem
        {
            Title = "Quick thought",
            Description = "Capture this idea",
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == "Quick thought");
        Assert.NotNull(retrieved);
        Assert.Equal("Quick thought", retrieved.Title);
        Assert.Equal("Capture this idea", retrieved.Description);
    }

    [Fact]
    public async Task InboxItem_DefaultStatus_IsUnprocessed()
    {
        var item = new InboxItem
        {
            Title = "Default Status",
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == "Default Status");
        Assert.NotNull(retrieved);
        Assert.Equal(InboxItemStatus.Unprocessed, retrieved.Status);
    }

    [Fact]
    public async Task InboxItem_Title_IsRequired()
    {
        var item = new InboxItem
        {
            Title = null!,
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task InboxItem_Description_IsOptional()
    {
        var item = new InboxItem
        {
            Title = "No Description",
            Description = null,
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == "No Description");
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Description);
    }

    [Theory]
    [InlineData(InboxItemStatus.Unprocessed)]
    [InlineData(InboxItemStatus.Processed)]
    [InlineData(InboxItemStatus.Dismissed)]
    public async Task InboxItem_CanHaveAllStatuses(InboxItemStatus status)
    {
        var item = new InboxItem
        {
            Title = $"Status {status}",
            Status = status,
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == $"Status {status}");
        Assert.NotNull(retrieved);
        Assert.Equal(status, retrieved.Status);
    }

    [Fact]
    public async Task InboxItem_ConvertedToTaskId_IsOptional()
    {
        var item = new InboxItem
        {
            Title = "Not Converted",
            UserId = _user.Id,
            ConvertedToTaskId = null
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == "Not Converted");
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.ConvertedToTaskId);
    }

    [Fact]
    public async Task InboxItem_CanLinkToConvertedTask()
    {
        var project = new Project { Name = "Test Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = "Converted Task",
            ProjectId = project.Id,
            CreatedByUserId = _user.Id
        };
        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var item = new InboxItem
        {
            Title = "Converted Item",
            UserId = _user.Id,
            Status = InboxItemStatus.Processed,
            ConvertedToTaskId = task.Id
        };
        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems
            .Include(i => i.ConvertedToTask)
            .FirstOrDefaultAsync(i => i.Title == "Converted Item");
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.ConvertedToTask);
        Assert.Equal("Converted Task", retrieved.ConvertedToTask.Title);
    }

    [Fact]
    public async Task InboxItem_ConvertedToTask_SetNull_WhenTaskDeleted()
    {
        var project = new Project { Name = "Delete Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = "Task To Delete",
            ProjectId = project.Id,
            CreatedByUserId = _user.Id
        };
        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var item = new InboxItem
        {
            Title = "Linked Item",
            UserId = _user.Id,
            Status = InboxItemStatus.Processed,
            ConvertedToTaskId = task.Id
        };
        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        _context.TaskItems.Remove(task);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == "Linked Item");
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.ConvertedToTaskId);
    }

    [Fact]
    public async Task InboxItem_BelongsToUser()
    {
        var item = new InboxItem
        {
            Title = "User Item",
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems
            .Include(i => i.User)
            .FirstOrDefaultAsync(i => i.Title == "User Item");
        Assert.NotNull(retrieved);
        Assert.Equal(_user.Id, retrieved.UserId);
        Assert.Equal("Test User", retrieved.User.DisplayName);
    }

    [Fact]
    public async Task InboxItem_CascadeDelete_WhenUserDeleted()
    {
        var user = new ApplicationUser
        {
            UserName = "delete@test.com",
            Email = "delete@test.com",
            DisplayName = "Delete User"
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.InboxItems.Add(new InboxItem
        {
            Title = "Cascade Item",
            UserId = user.Id
        });
        await _context.SaveChangesAsync();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        var items = await _context.InboxItems.Where(i => i.UserId == user.Id).ToListAsync();
        Assert.Empty(items);
    }

    [Fact]
    public async Task InboxItem_SetsTimestamps()
    {
        var beforeCreate = DateTime.UtcNow.AddSeconds(-1);

        var item = new InboxItem
        {
            Title = "Timestamp Item",
            UserId = _user.Id
        };

        _context.InboxItems.Add(item);
        await _context.SaveChangesAsync();

        var retrieved = await _context.InboxItems.FirstOrDefaultAsync(i => i.Title == "Timestamp Item");
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= beforeCreate);
        Assert.True(retrieved.UpdatedAt >= beforeCreate);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
