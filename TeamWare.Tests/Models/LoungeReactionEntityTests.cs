using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class LoungeReactionEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _user;
    private readonly ApplicationUser _user2;
    private readonly Project _project;
    private readonly LoungeMessage _message;

    public LoungeReactionEntityTests()
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
        _user2 = new ApplicationUser
        {
            UserName = "test2@test.com",
            Email = "test2@test.com",
            DisplayName = "Test User 2"
        };
        _context.Users.Add(_user);
        _context.Users.Add(_user2);
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
    public async Task CanCreateLoungeReaction()
    {
        var reaction = new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        };

        _context.LoungeReactions.Add(reaction);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReactions.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("thumbsup", retrieved.ReactionType);
        Assert.Equal(_message.Id, retrieved.LoungeMessageId);
        Assert.Equal(_user.Id, retrieved.UserId);
    }

    [Fact]
    public async Task LoungeReaction_HasTimestamp()
    {
        var before = DateTime.UtcNow;

        var reaction = new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "heart"
        };

        _context.LoungeReactions.Add(reaction);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReactions.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= before);
    }

    [Fact]
    public async Task LoungeReaction_NavigationToMessage()
    {
        var reaction = new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        };

        _context.LoungeReactions.Add(reaction);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReactions
            .Include(r => r.LoungeMessage)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.LoungeMessage);
        Assert.Equal(_message.Content, retrieved.LoungeMessage.Content);
    }

    [Fact]
    public async Task LoungeReaction_NavigationToUser()
    {
        var reaction = new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        };

        _context.LoungeReactions.Add(reaction);
        await _context.SaveChangesAsync();

        var retrieved = await _context.LoungeReactions
            .Include(r => r.User)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.User);
        Assert.Equal(_user.DisplayName, retrieved.User.DisplayName);
    }

    [Fact]
    public async Task LoungeReaction_UniqueConstraint_SameUserSameTypeSameMessage()
    {
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        });
        await _context.SaveChangesAsync();

        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task LoungeReaction_DifferentUsersCanUseSameType()
    {
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        });
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user2.Id,
            ReactionType = "thumbsup"
        });
        await _context.SaveChangesAsync();

        var reactions = await _context.LoungeReactions.ToListAsync();
        Assert.Equal(2, reactions.Count);
    }

    [Fact]
    public async Task LoungeReaction_SameUserCanUseDifferentTypes()
    {
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        });
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "heart"
        });
        await _context.SaveChangesAsync();

        var reactions = await _context.LoungeReactions.ToListAsync();
        Assert.Equal(2, reactions.Count);
    }

    [Fact]
    public async Task LoungeReaction_CascadeDeleteWithMessage()
    {
        _context.LoungeReactions.Add(new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = "thumbsup"
        });
        await _context.SaveChangesAsync();

        _context.LoungeMessages.Remove(_message);
        await _context.SaveChangesAsync();

        var reactions = await _context.LoungeReactions.ToListAsync();
        Assert.Empty(reactions);
    }

    [Fact]
    public void LoungeReaction_ReactionTypeIsRequired()
    {
        var reaction = new LoungeReaction
        {
            LoungeMessageId = _message.Id,
            UserId = _user.Id,
            ReactionType = string.Empty
        };

        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(reaction);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            reaction, validationContext, validationResults, true);

        Assert.False(isValid);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
