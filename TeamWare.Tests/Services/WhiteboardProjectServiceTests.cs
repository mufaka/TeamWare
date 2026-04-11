using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class WhiteboardProjectServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly WhiteboardProjectService _service;
    private readonly ProjectMemberService _projectMemberService;

    public WhiteboardProjectServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        var notificationService = new NotificationService(_context);
        var invitationService = new WhiteboardInvitationService(_context, notificationService);
        _service = new WhiteboardProjectService(_context, invitationService);
        _projectMemberService = new ProjectMemberService(_context, _service);
    }

    [Fact]
    public async Task SaveToProjectAsync_AsOwnerAndProjectMember_SucceedsAndCleansInvalidInvitations()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var validMember = CreateUser("member@test.com", "Member");
        var invalidUser = CreateUser("invalid@test.com", "Invalid");
        var project = await CreateProjectAsync("Project Board");
        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        AddProjectMember(project.Id, validMember.Id, ProjectRole.Member);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Project Save Board");

        _context.WhiteboardInvitations.AddRange(
            new WhiteboardInvitation
            {
                WhiteboardId = whiteboard.Id,
                UserId = validMember.Id,
                InvitedByUserId = owner.Id,
                CreatedAt = DateTime.UtcNow
            },
            new WhiteboardInvitation
            {
                WhiteboardId = whiteboard.Id,
                UserId = invalidUser.Id,
                InvitedByUserId = owner.Id,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync();

        var result = await _service.SaveToProjectAsync(whiteboard.Id, project.Id, owner.Id);

        Assert.True(result.Succeeded);
        var updatedWhiteboard = await _context.Whiteboards.FindAsync(whiteboard.Id);
        Assert.Equal(project.Id, updatedWhiteboard!.ProjectId);

        var invitations = await _context.WhiteboardInvitations
            .Where(i => i.WhiteboardId == whiteboard.Id)
            .ToListAsync();
        Assert.Single(invitations);
        Assert.Equal(validMember.Id, invitations[0].UserId);
    }

    [Fact]
    public async Task SaveToProjectAsync_AsNonOwner_Fails()
    {
        var owner = CreateUser("owner2@test.com", "Owner");
        var user = CreateUser("user@test.com", "User");
        var project = await CreateProjectAsync("Project Board");
        AddProjectMember(project.Id, user.Id, ProjectRole.Member);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Protected Board");

        var result = await _service.SaveToProjectAsync(whiteboard.Id, project.Id, user.Id);

        Assert.False(result.Succeeded);
        Assert.Null((await _context.Whiteboards.FindAsync(whiteboard.Id))!.ProjectId);
    }

    [Fact]
    public async Task SaveToProjectAsync_WhenOwnerIsNotProjectMember_Fails()
    {
        var owner = CreateUser("owner3@test.com", "Owner");
        var project = await CreateProjectAsync("Restricted Project");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Restricted Board");

        var result = await _service.SaveToProjectAsync(whiteboard.Id, project.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("project members"));
    }

    [Fact]
    public async Task ClearProjectAsync_AsOwner_RemovesProjectAssociation()
    {
        var owner = CreateUser("owner4@test.com", "Owner");
        var project = await CreateProjectAsync("Clear Project");
        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Saved Board", project.Id);

        var result = await _service.ClearProjectAsync(whiteboard.Id, owner.Id);

        Assert.True(result.Succeeded);
        var updatedWhiteboard = await _context.Whiteboards.FindAsync(whiteboard.Id);
        Assert.Null(updatedWhiteboard!.ProjectId);
    }

    [Fact]
    public async Task ClearProjectAsync_AsNonOwner_Fails()
    {
        var owner = CreateUser("owner5@test.com", "Owner");
        var user = CreateUser("user5@test.com", "User");
        var project = await CreateProjectAsync("Clear Project");
        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Saved Board", project.Id);

        var result = await _service.ClearProjectAsync(whiteboard.Id, user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(project.Id, (await _context.Whiteboards.FindAsync(whiteboard.Id))!.ProjectId);
    }

    [Fact]
    public async Task TransferOwnershipIfNeededAsync_TransfersToProjectOwner_WhenOwnerLostMembership()
    {
        var previousOwner = CreateUser("owner6@test.com", "Previous Owner");
        var projectOwner = CreateUser("project-owner@test.com", "Project Owner");
        var project = await CreateProjectAsync("Transfer Project");
        AddProjectMember(project.Id, projectOwner.Id, ProjectRole.Owner);
        var whiteboard = await CreateWhiteboardAsync(previousOwner.Id, "Transferred Board", project.Id);

        var result = await _service.TransferOwnershipIfNeededAsync(whiteboard.Id);

        Assert.True(result.Succeeded);
        var updatedWhiteboard = await _context.Whiteboards.FindAsync(whiteboard.Id);
        Assert.Equal(projectOwner.Id, updatedWhiteboard!.OwnerId);
    }

    [Fact]
    public async Task TransferOwnershipIfNeededAsync_WhenOwnerStillMember_DoesNothing()
    {
        var owner = CreateUser("owner7@test.com", "Owner");
        var project = await CreateProjectAsync("Stable Project");
        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Stable Board", project.Id);

        var result = await _service.TransferOwnershipIfNeededAsync(whiteboard.Id);

        Assert.True(result.Succeeded);
        var updatedWhiteboard = await _context.Whiteboards.FindAsync(whiteboard.Id);
        Assert.Equal(owner.Id, updatedWhiteboard!.OwnerId);
    }

    [Fact]
    public async Task RemoveMember_TransfersOwnershipOfSavedWhiteboardsToProjectOwner()
    {
        var projectOwner = CreateUser("owner8@test.com", "Project Owner");
        var removedOwner = CreateUser("removed-owner@test.com", "Removed Owner");
        var project = await CreateProjectAsync("Ownership Transfer Hook Project");
        AddProjectMember(project.Id, projectOwner.Id, ProjectRole.Owner);
        AddProjectMember(project.Id, removedOwner.Id, ProjectRole.Member);
        var whiteboard = await CreateWhiteboardAsync(removedOwner.Id, "Transferred by Member Removal", project.Id);

        var result = await _projectMemberService.RemoveMember(project.Id, removedOwner.Id, projectOwner.Id);

        Assert.True(result.Succeeded);
        var updatedWhiteboard = await _context.Whiteboards.FindAsync(whiteboard.Id);
        Assert.Equal(projectOwner.Id, updatedWhiteboard!.OwnerId);
    }

    [Fact]
    public async Task DeletingProject_CascadesToAssociatedWhiteboards()
    {
        var owner = CreateUser("owner9@test.com", "Owner");
        var project = await CreateProjectAsync("Cascade Project");
        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Cascade Whiteboard", project.Id);

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        Assert.Null(await _context.Whiteboards.FindAsync(whiteboard.Id));
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
