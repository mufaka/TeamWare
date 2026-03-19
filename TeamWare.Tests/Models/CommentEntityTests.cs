using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class CommentEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly Project _project;
    private readonly TaskItem _task;

    public CommentEntityTests()
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
    public async Task CanCreateComment()
    {
        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "This is a test comment."
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Comments.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("This is a test comment.", retrieved.Content);
        Assert.Equal(_task.Id, retrieved.TaskItemId);
        Assert.Equal(_user.Id, retrieved.AuthorId);
    }

    [Fact]
    public async Task Comment_HasTimestamps()
    {
        var before = DateTime.UtcNow;

        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "Timestamped comment"
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Comments.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= before);
        Assert.True(retrieved.UpdatedAt >= before);
    }

    [Fact]
    public async Task Comment_NavigationToTaskItem()
    {
        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "Navigation test"
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Comments
            .Include(c => c.TaskItem)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.TaskItem);
        Assert.Equal(_task.Title, retrieved.TaskItem.Title);
    }

    [Fact]
    public async Task Comment_NavigationToAuthor()
    {
        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "Author navigation test"
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Comments
            .Include(c => c.Author)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Author);
        Assert.Equal(_user.DisplayName, retrieved.Author.DisplayName);
    }

    [Fact]
    public async Task Comment_CascadeDeleteWithTask()
    {
        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "Will be cascade deleted"
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        _context.TaskItems.Remove(_task);
        await _context.SaveChangesAsync();

        var comments = await _context.Comments.ToListAsync();
        Assert.Empty(comments);
    }

    [Fact]
    public async Task TaskItem_CommentsNavigation()
    {
        _context.Comments.Add(new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "First comment"
        });
        _context.Comments.Add(new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = "Second comment"
        });
        await _context.SaveChangesAsync();

        var task = await _context.TaskItems
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == _task.Id);

        Assert.NotNull(task);
        Assert.Equal(2, task.Comments.Count);
    }

    [Fact]
    public async Task Comment_ContentMaxLength5000()
    {
        var longContent = new string('A', 5000);
        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = longContent
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Comments.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(5000, retrieved.Content.Length);
    }

    [Fact]
    public async Task Comment_ContentIsRequired()
    {
        var comment = new Comment
        {
            TaskItemId = _task.Id,
            AuthorId = _user.Id,
            Content = string.Empty
        };

        _context.Comments.Add(comment);

        // EF Core should enforce the Required attribute via validation
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(comment);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            comment, validationContext, validationResults, true);

        Assert.False(isValid);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
