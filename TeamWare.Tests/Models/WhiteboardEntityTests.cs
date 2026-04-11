using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class WhiteboardEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public WhiteboardEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.Migrate();
    }

    [Fact]
    public async Task Migration_AppliesCleanly_AndExistingEntitiesRemainUsable()
    {
        using var context = CreateContext();

        var user = CreateUser("existing-user", "Existing User");
        var project = new Project
        {
            Name = "Existing Project",
            Description = "Project created after whiteboard migration"
        };

        context.Users.Add(user);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var task = new TaskItem
        {
            ProjectId = project.Id,
            Title = "Existing Task",
            CreatedByUserId = user.Id
        };

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var retrievedTask = await context.TaskItems
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == task.Id);

        Assert.NotNull(retrievedTask);
        Assert.Equal("Existing Task", retrievedTask.Title);
        Assert.Equal("Existing Project", retrievedTask.Project.Name);
    }

    [Fact]
    public async Task CanPerformCrudOperations_OnWhiteboardEntities()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner-user", "Owner User");
        var invitee = CreateUser("invitee-user", "Invitee User");
        var project = new Project { Name = "Whiteboard Project" };

        context.Users.AddRange(owner, invitee);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var whiteboard = new Whiteboard
        {
            Title = "Architecture Board",
            OwnerId = owner.Id,
            ProjectId = project.Id,
            CurrentPresenterId = owner.Id,
            CanvasData = "{\"shapes\":[] }"
        };

        context.Whiteboards.Add(whiteboard);
        await context.SaveChangesAsync();

        var invitation = new WhiteboardInvitation
        {
            WhiteboardId = whiteboard.Id,
            UserId = invitee.Id,
            InvitedByUserId = owner.Id
        };

        var message = new WhiteboardChatMessage
        {
            WhiteboardId = whiteboard.Id,
            UserId = invitee.Id,
            Content = "Initial comment"
        };

        context.WhiteboardInvitations.Add(invitation);
        context.WhiteboardChatMessages.Add(message);
        await context.SaveChangesAsync();

        var retrievedWhiteboard = await context.Whiteboards
            .Include(w => w.Owner)
            .Include(w => w.Project)
            .Include(w => w.CurrentPresenter)
            .Include(w => w.Invitations)
            .Include(w => w.ChatMessages)
            .FirstOrDefaultAsync(w => w.Id == whiteboard.Id);

        Assert.NotNull(retrievedWhiteboard);
        Assert.Equal("Architecture Board", retrievedWhiteboard.Title);
        Assert.Equal(owner.Id, retrievedWhiteboard.OwnerId);
        Assert.Equal(project.Id, retrievedWhiteboard.ProjectId);
        Assert.Equal(owner.Id, retrievedWhiteboard.CurrentPresenterId);
        Assert.Single(retrievedWhiteboard.Invitations);
        Assert.Single(retrievedWhiteboard.ChatMessages);

        retrievedWhiteboard.CanvasData = "{\"shapes\":[{\"id\":1}] }";
        retrievedWhiteboard.UpdatedAt = retrievedWhiteboard.UpdatedAt.AddMinutes(1);
        message.Content = "Updated comment";
        await context.SaveChangesAsync();

        var updatedMessage = await context.WhiteboardChatMessages.FindAsync(message.Id);
        Assert.NotNull(updatedMessage);
        Assert.Equal("Updated comment", updatedMessage.Content);

        context.WhiteboardInvitations.Remove(invitation);
        context.WhiteboardChatMessages.Remove(message);
        await context.SaveChangesAsync();

        Assert.Null(await context.WhiteboardInvitations.FindAsync(invitation.Id));
        Assert.Null(await context.WhiteboardChatMessages.FindAsync(message.Id));

        context.Whiteboards.Remove(retrievedWhiteboard);
        await context.SaveChangesAsync();

        Assert.Null(await context.Whiteboards.FindAsync(whiteboard.Id));
    }

    [Fact]
    public async Task DeletingWhiteboard_CascadesToInvitationsAndChatMessages()
    {
        using var context = CreateContext();

        var owner = CreateUser("cascade-owner", "Cascade Owner");
        var participant = CreateUser("cascade-participant", "Cascade Participant");

        context.Users.AddRange(owner, participant);
        await context.SaveChangesAsync();

        var whiteboard = new Whiteboard
        {
            Title = "Temporary Board",
            OwnerId = owner.Id,
            CurrentPresenterId = owner.Id
        };

        context.Whiteboards.Add(whiteboard);
        await context.SaveChangesAsync();

        context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = whiteboard.Id,
            UserId = participant.Id,
            InvitedByUserId = owner.Id
        });

        context.WhiteboardChatMessages.Add(new WhiteboardChatMessage
        {
            WhiteboardId = whiteboard.Id,
            UserId = participant.Id,
            Content = "Hello"
        });

        await context.SaveChangesAsync();

        context.Whiteboards.Remove(whiteboard);
        await context.SaveChangesAsync();

        Assert.Empty(await context.WhiteboardInvitations.ToListAsync());
        Assert.Empty(await context.WhiteboardChatMessages.ToListAsync());
    }

    [Fact]
    public async Task DeletingProject_CascadesToAssociatedWhiteboards()
    {
        using var context = CreateContext();

        var owner = CreateUser("project-owner", "Project Owner");
        var project = new Project { Name = "Project With Board" };

        context.Users.Add(owner);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var whiteboard = new Whiteboard
        {
            Title = "Saved Board",
            OwnerId = owner.Id,
            ProjectId = project.Id,
            CurrentPresenterId = owner.Id
        };

        context.Whiteboards.Add(whiteboard);
        await context.SaveChangesAsync();

        context.Projects.Remove(project);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Whiteboards.ToListAsync());
    }

    [Fact]
    public async Task WhiteboardInvitation_RequiresUniqueWhiteboardIdAndUserId()
    {
        using var context = CreateContext();

        var owner = CreateUser("unique-owner", "Unique Owner");
        var invitee = CreateUser("unique-invitee", "Unique Invitee");

        context.Users.AddRange(owner, invitee);
        await context.SaveChangesAsync();

        var whiteboard = new Whiteboard
        {
            Title = "Unique Invite Board",
            OwnerId = owner.Id,
            CurrentPresenterId = owner.Id
        };

        context.Whiteboards.Add(whiteboard);
        await context.SaveChangesAsync();

        context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = whiteboard.Id,
            UserId = invitee.Id,
            InvitedByUserId = owner.Id
        });

        await context.SaveChangesAsync();

        context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = whiteboard.Id,
            UserId = invitee.Id,
            InvitedByUserId = owner.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private ApplicationDbContext CreateContext()
    {
        return new ApplicationDbContext(_options);
    }

    private static ApplicationUser CreateUser(string id, string displayName)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@example.com",
            NormalizedUserName = $"{id}@EXAMPLE.COM",
            Email = $"{id}@example.com",
            NormalizedEmail = $"{id}@EXAMPLE.COM",
            DisplayName = displayName
        };
    }
}
