using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class WhiteboardInvitationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly NotificationService _notificationService;
    private readonly WhiteboardInvitationService _service;

    public WhiteboardInvitationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notificationService = new NotificationService(_context);
        _service = new WhiteboardInvitationService(_context, _notificationService);
    }

    [Fact]
    public async Task InviteAsync_AsOwner_CreatesInvitationAndNotification()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var invitee = CreateUser("invitee@test.com", "Invitee");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Invite Board");

        var result = await _service.InviteAsync(whiteboard.Id, invitee.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.True(await _service.HasInvitationAsync(whiteboard.Id, invitee.Id));

        var notifications = await _notificationService.GetUnreadForUser(invitee.Id);
        Assert.Single(notifications);
        Assert.Equal(NotificationType.WhiteboardInvitation, notifications[0].Type);
        Assert.Equal(whiteboard.Id, notifications[0].ReferenceId);
        Assert.Contains("Invite Board", notifications[0].Message);
    }

    [Fact]
    public async Task InviteAsync_AsNonOwner_Fails()
    {
        var owner = CreateUser("owner2@test.com", "Owner");
        var invitee = CreateUser("invitee2@test.com", "Invitee");
        var user = CreateUser("user@test.com", "User");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Protected Board");

        var result = await _service.InviteAsync(whiteboard.Id, invitee.Id, user.Id);

        Assert.False(result.Succeeded);
        Assert.False(await _service.HasInvitationAsync(whiteboard.Id, invitee.Id));
    }

    [Fact]
    public async Task InviteAsync_DuplicateInvitation_FailsGracefully()
    {
        var owner = CreateUser("owner3@test.com", "Owner");
        var invitee = CreateUser("invitee3@test.com", "Invitee");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Duplicate Board");

        await _service.InviteAsync(whiteboard.Id, invitee.Id, owner.Id);
        var result = await _service.InviteAsync(whiteboard.Id, invitee.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("already invited"));
    }

    [Fact]
    public async Task RevokeAsync_RemovesInvitation()
    {
        var owner = CreateUser("owner4@test.com", "Owner");
        var invitee = CreateUser("invitee4@test.com", "Invitee");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Revoke Board");
        await _service.InviteAsync(whiteboard.Id, invitee.Id, owner.Id);

        var result = await _service.RevokeAsync(whiteboard.Id, invitee.Id);

        Assert.True(result.Succeeded);
        Assert.False(await _service.HasInvitationAsync(whiteboard.Id, invitee.Id));
    }

    [Fact]
    public async Task HasInvitationAsync_ReturnsFalseWhenInvitationDoesNotExist()
    {
        var owner = CreateUser("owner5@test.com", "Owner");
        var invitee = CreateUser("invitee5@test.com", "Invitee");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Lookup Board");

        var result = await _service.HasInvitationAsync(whiteboard.Id, invitee.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task CleanupInvalidInvitationsAsync_RemovesUsersWhoAreNoLongerProjectMembers()
    {
        var owner = CreateUser("owner6@test.com", "Owner");
        var member = CreateUser("member@test.com", "Member");
        var removedUser = CreateUser("removed@test.com", "Removed");
        var project = await CreateProjectAsync("Cleanup Project");
        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        AddProjectMember(project.Id, member.Id, ProjectRole.Member);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Cleanup Board", project.Id);

        _context.WhiteboardInvitations.AddRange(
            new WhiteboardInvitation
            {
                WhiteboardId = whiteboard.Id,
                UserId = member.Id,
                InvitedByUserId = owner.Id,
                CreatedAt = DateTime.UtcNow
            },
            new WhiteboardInvitation
            {
                WhiteboardId = whiteboard.Id,
                UserId = removedUser.Id,
                InvitedByUserId = owner.Id,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync();

        var result = await _service.CleanupInvalidInvitationsAsync(whiteboard.Id);

        Assert.True(result.Succeeded);
        var remainingInvitations = await _context.WhiteboardInvitations
            .Where(i => i.WhiteboardId == whiteboard.Id)
            .ToListAsync();
        Assert.Single(remainingInvitations);
        Assert.Equal(member.Id, remainingInvitations[0].UserId);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private ApplicationUser CreateUser(string email, string displayName)
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

    private async Task<Project> CreateProjectAsync(string name)
    {
        var project = new Project { Name = name };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();
        return project;
    }

    private void AddProjectMember(int projectId, string userId, ProjectRole role)
    {
        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    private async Task<Whiteboard> CreateWhiteboardAsync(string ownerId, string title, int? projectId = null)
    {
        var whiteboard = new Whiteboard
        {
            Title = title,
            OwnerId = ownerId,
            ProjectId = projectId,
            CurrentPresenterId = ownerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Whiteboards.Add(whiteboard);
        await _context.SaveChangesAsync();
        return whiteboard;
    }
}
