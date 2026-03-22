using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class LoungeReadPositionEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly Project _project;
    private readonly LoungeMessage _message;

    public LoungeReadPositionEntityTests()
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

        _message = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Test message"
        };
        _context.LoungeMessages.Add(_message);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateLoungeReadPosition()
    {
        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(_user.Id, retrieved.UserId);
        Assert.Equal(_project.Id, retrieved.ProjectId);
        Assert.Equal(_message.Id, retrieved.LastReadMessageId);
    }

    [Fact]
    public async Task CanCreateReadPosition_ForGeneralRoom_NullProjectId()
    {
        var generalMessage = new LoungeMessage
        {
            ProjectId = null,
            UserId = _user.Id,
            Content = "General room message"
        };
        _context.LoungeMessages.Add(generalMessage);
        await _context.SaveChangesAsync();

        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = null,
            LastReadMessageId = generalMessage.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.ProjectId);
    }

    [Fact]
    public async Task LoungeReadPosition_HasTimestamp()
    {
        var before = DateTime.UtcNow;

        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.UpdatedAt >= before);
    }

    [Fact]
    public async Task LoungeReadPosition_NavigationToUser()
    {
        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions
            .Include(rp => rp.User)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.User);
        Assert.Equal(_user.DisplayName, retrieved.User.DisplayName);
    }

    [Fact]
    public async Task LoungeReadPosition_NavigationToProject()
    {
        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions
            .Include(rp => rp.Project)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Project);
        Assert.Equal(_project.Name, retrieved.Project.Name);
    }

    [Fact]
    public async Task LoungeReadPosition_NavigationToLastReadMessage()
    {
        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions
            .Include(rp => rp.LastReadMessage)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.LastReadMessage);
        Assert.Equal(_message.Content, retrieved.LastReadMessage.Content);
    }

    [Fact]
    public async Task LoungeReadPosition_UniqueConstraint_SameUserSameProject()
    {
        _context.LoungeReadPositions.Add(new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        });
        await _context.SaveChangesAsync();

        var message2 = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Second message"
        };
        _context.LoungeMessages.Add(message2);
        await _context.SaveChangesAsync();

        _context.LoungeReadPositions.Add(new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = message2.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task LoungeReadPosition_CanUpdateLastReadMessage()
    {
        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        var message2 = new LoungeMessage
        {
            ProjectId = _project.Id,
            UserId = _user.Id,
            Content = "Second message"
        };
        _context.LoungeMessages.Add(message2);
        await _context.SaveChangesAsync();

        readPosition.LastReadMessageId = message2.Id;
        readPosition.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReadPositions.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal(message2.Id, retrieved.LastReadMessageId);
    }

    [Fact]
    public async Task LoungeReadPosition_CascadeDeleteWithMessage()
    {
        var readPosition = new LoungeReadPosition
        {
            UserId = _user.Id,
            ProjectId = _project.Id,
            LastReadMessageId = _message.Id
        };

        _context.LoungeReadPositions.Add(readPosition);
        await _context.SaveChangesAsync();

        _context.LoungeMessages.Remove(_message);
        await _context.SaveChangesAsync();

        var positions = await _context.LoungeReadPositions.ToListAsync();
        Assert.Empty(positions);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
