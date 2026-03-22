using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class LoungeMessageEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly ApplicationUser _pinnerUser;
    private readonly Project _project;

    public LoungeMessageEntityTests()
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
        _pinnerUser = new ApplicationUser
        {
            UserName = "pinner@test.com",
            Email = "pinner@test.com",
            DisplayName = "Pinner User"
        };
        _context.Users.Add(_user);
        _context.Users.Add(_pinnerUser);
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
    }

    [Fact]
    public async Task CanCreateLoungeMessage()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Hello, lounge!"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("Hello, lounge!", retrieved.Content);
        Assert.Equal(_project.Id, retrieved.ProjectId);
        Assert.Equal(_user.Id, retrieved.UserId);
    }

    [Fact]
    public async Task CanCreateGeneralRoomMessage_NullProjectId()
    {
        var message = new LoungeMessage
        {
            ProjectId = null,
            UserId = _user.Id,
            Content = "Hello, #general!"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.ProjectId);
    }

    [Fact]
    public async Task LoungeMessage_HasTimestamps()
    {
        var before = DateTime.UtcNow;

        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Timestamped message"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= before);
    }

    [Fact]
    public async Task LoungeMessage_NavigationToProject()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Navigation test"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages
            .Include(m => m.Project)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Project);
        Assert.Equal(_project.Name, retrieved.Project.Name);
    }

    [Fact]
    public async Task LoungeMessage_NavigationToUser()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Author navigation test"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages
            .Include(m => m.User)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.User);
        Assert.Equal(_user.DisplayName, retrieved.User.DisplayName);
    }

    [Fact]
    public async Task LoungeMessage_EditFields()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Original content"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        message.Content = "Edited content";
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.IsEdited);
        Assert.NotNull(retrieved.EditedAt);
        Assert.Equal("Edited content", retrieved.Content);
    }

    [Fact]
    public async Task LoungeMessage_PinFields()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Pinnable message"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        message.IsPinned = true;
        message.PinnedByUserId = _pinnerUser.Id;
        message.PinnedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages
            .Include(m => m.PinnedByUser)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.True(retrieved.IsPinned);
        Assert.Equal(_pinnerUser.Id, retrieved.PinnedByUserId);
        Assert.NotNull(retrieved.PinnedAt);
        Assert.NotNull(retrieved.PinnedByUser);
    }

    [Fact]
    public async Task LoungeMessage_CreatedTaskNavigation()
    {
        var task = new TaskItem
        {
            Title = "Task from message",
            ProjectId = _project.Id,
            CreatedByUserId = _user.Id,
            Status = TaskItemStatus.ToDo,
            Priority = TaskItemPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync();

        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Create task from this",
            CreatedTaskId = task.Id
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages
            .Include(m => m.CreatedTask)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.CreatedTask);
        Assert.Equal("Task from message", retrieved.CreatedTask.Title);
    }

    [Fact]
    public async Task LoungeMessage_ContentMaxLength4000()
    {
        var longContent = new string('A', 4000);
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = longContent
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(4000, retrieved.Content.Length);
    }

    [Fact]
    public void LoungeMessage_ContentIsRequired()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = string.Empty
        };

        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(message);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            message, validationContext, validationResults, true);

        Assert.False(isValid);
    }

    [Fact]
    public async Task Project_LoungeMessagesNavigation()
    {
        _context.LoungeMessages.Add(new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "First message"
        });
        _context.LoungeMessages.Add(new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Second message"
        });
        await _context.SaveChangesAsync();

        var project = await _context.Projects
            .Include(p => p.LoungeMessages)
            .FirstOrDefaultAsync(p => p.Id == _project.Id);

        Assert.NotNull(project);
        Assert.Equal(2, project.LoungeMessages.Count);
    }

    [Fact]
    public async Task ApplicationUser_LoungeMessagesNavigation()
    {
        _context.LoungeMessages.Add(new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "User message 1"
        });
        _context.LoungeMessages.Add(new LoungeMessage
        {
            ProjectId = null,
            UserId = _user.Id,
            Content = "User message 2"
        });
        await _context.SaveChangesAsync();

        var user = await _context.Users
            .Include(u => u.LoungeMessages)
            .FirstOrDefaultAsync(u => u.Id == _user.Id);

        Assert.NotNull(user);
        Assert.Equal(2, user.LoungeMessages.Count);
    }

    [Fact]
    public async Task LoungeMessage_ReactionsNavigation()
    {
        var message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "React to this"
        };

        _context.LoungeMessages.Add(message);
        await _context.SaveChangesAsync();

        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        });
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeMessages
            .Include(m => m.Reactions)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.Single(retrieved.Reactions);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
