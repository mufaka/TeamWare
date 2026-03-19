using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class TaskAssignmentEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user1;
    private readonly ApplicationUser _user2;
    private readonly Project _project;
    private readonly TaskItem _task;

    public TaskAssignmentEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _user1 = new ApplicationUser { UserName = "user1@test.com", Email = "user1@test.com", DisplayName = "User One" };
        _user2 = new ApplicationUser { UserName = "user2@test.com", Email = "user2@test.com", DisplayName = "User Two" };
        _context.Users.AddRange(_user1, _user2);

        _project = new Project { Name = "Test Project" };
        _context.Projects.Add(_project);
        _context.SaveChanges();

        _task = new TaskItem
        {
            Title = "Test Task",
            ProjectId = _project.Id,
            CreatedByUserId = _user1.Id
        };
        _context.TaskItems.Add(_task);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateTaskAssignment()
    {
        var assignment = new TaskAssignment
        {
            TaskItemId = _task.Id,
            UserId = _user1.Id
        };

        _context.TaskAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskAssignments
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.TaskItemId == _task.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("User One", retrieved.User.DisplayName);
    }

    [Fact]
    public async Task TaskAssignment_UniqueConstraint_TaskAndUser()
    {
        _context.TaskAssignments.Add(new TaskAssignment { TaskItemId = _task.Id, UserId = _user1.Id });
        await _context.SaveChangesAsync();

        _context.TaskAssignments.Add(new TaskAssignment { TaskItemId = _task.Id, UserId = _user1.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task TaskItem_CanHaveMultipleAssignees()
    {
        _context.TaskAssignments.AddRange(
            new TaskAssignment { TaskItemId = _task.Id, UserId = _user1.Id },
            new TaskAssignment { TaskItemId = _task.Id, UserId = _user2.Id }
        );
        await _context.SaveChangesAsync();

        var retrieved = await _context.TaskItems
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == _task.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Assignments.Count);
    }

    [Fact]
    public async Task TaskAssignment_CascadeDelete_WhenTaskDeleted()
    {
        _context.TaskAssignments.Add(new TaskAssignment { TaskItemId = _task.Id, UserId = _user1.Id });
        await _context.SaveChangesAsync();

        _context.TaskItems.Remove(_task);
        await _context.SaveChangesAsync();

        var assignments = await _context.TaskAssignments.Where(a => a.TaskItemId == _task.Id).ToListAsync();
        Assert.Empty(assignments);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
