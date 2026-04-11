using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class WhiteboardServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly WhiteboardService _service;

    public WhiteboardServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new WhiteboardService(_context);
    }

    [Fact]
    public async Task CreateAsync_SetsOwnerAndInitialPresenter()
    {
        var owner = CreateUser("owner@test.com", "Owner");

        var result = await _service.CreateAsync(owner.Id, "  Architecture Board  ");

        Assert.True(result.Succeeded);
        var whiteboard = await _context.Whiteboards.FindAsync(result.Data);
        Assert.NotNull(whiteboard);
        Assert.Equal(owner.Id, whiteboard.OwnerId);
        Assert.Equal(owner.Id, whiteboard.CurrentPresenterId);
        Assert.Equal("Architecture Board", whiteboard.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsWhiteboardDetailsWithInvitations()
    {
        var owner = CreateUser("detail-owner@test.com", "Owner");
        var invitee = CreateUser("detail-invitee@test.com", "Invitee");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Detail Board");

        _context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = whiteboard.Id,
            UserId = invitee.Id,
            InvitedByUserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _service.GetByIdAsync(whiteboard.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Detail Board", result.Data!.Title);
        Assert.Single(result.Data.Invitations);
        Assert.Equal(invitee.Id, result.Data.Invitations[0].UserId);
        Assert.Equal("Invitee", result.Data.Invitations[0].UserDisplayName);
    }

    [Fact]
    public async Task GetLandingPageAsync_ReturnsOwnedInvitedAndProjectBoards()
    {
        var owner = CreateUser("owner-landing@test.com", "Owner");
        var invitedUser = CreateUser("invited@test.com", "Invited User");
        var projectMember = CreateUser("project-member@test.com", "Project Member");
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var project = await CreateProjectAsync("Saved Project");

        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        AddProjectMember(project.Id, projectMember.Id, ProjectRole.Member);

        var ownedBoard = await CreateWhiteboardAsync(owner.Id, "Owned Board", updatedAt: DateTime.UtcNow.AddMinutes(-3));
        var invitedBoard = await CreateWhiteboardAsync(owner.Id, "Invited Board", updatedAt: DateTime.UtcNow.AddMinutes(-2));
        var savedBoard = await CreateWhiteboardAsync(owner.Id, "Saved Board", project.Id, updatedAt: DateTime.UtcNow.AddMinutes(-1));

        _context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = invitedBoard.Id,
            UserId = invitedUser.Id,
            InvitedByUserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var invitedResult = await _service.GetLandingPageAsync(invitedUser.Id, false);
        var memberResult = await _service.GetLandingPageAsync(projectMember.Id, false);
        var ownerResult = await _service.GetLandingPageAsync(owner.Id, false);
        var outsiderResult = await _service.GetLandingPageAsync(outsider.Id, false);

        Assert.True(invitedResult.Succeeded);
        Assert.Single(invitedResult.Data!);
        Assert.Equal(invitedBoard.Id, invitedResult.Data[0].Id);

        Assert.True(memberResult.Succeeded);
        Assert.Single(memberResult.Data!);
        Assert.Equal(savedBoard.Id, memberResult.Data[0].Id);

        Assert.True(ownerResult.Succeeded);
        Assert.Equal(3, ownerResult.Data!.Count);
        Assert.Equal(savedBoard.Id, ownerResult.Data[0].Id);

        Assert.True(outsiderResult.Succeeded);
        Assert.Empty(outsiderResult.Data!);
    }

    [Fact]
    public async Task GetLandingPageAsync_AsSiteAdmin_ReturnsAllBoards()
    {
        var owner = CreateUser("admin-owner@test.com", "Owner");
        var admin = CreateUser("admin@test.com", "Admin");

        await CreateWhiteboardAsync(owner.Id, "Board One");
        await CreateWhiteboardAsync(owner.Id, "Board Two");

        var result = await _service.GetLandingPageAsync(admin.Id, true);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task DeleteAsync_AsOwner_RemovesWhiteboard()
    {
        var owner = CreateUser("delete-owner@test.com", "Owner");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Delete Board");

        var result = await _service.DeleteAsync(whiteboard.Id, owner.Id, false);

        Assert.True(result.Succeeded);
        Assert.Null(await _context.Whiteboards.FindAsync(whiteboard.Id));
    }

    [Fact]
    public async Task DeleteAsync_AsSiteAdmin_RemovesWhiteboard()
    {
        var owner = CreateUser("delete-admin-owner@test.com", "Owner");
        var admin = CreateUser("delete-admin@test.com", "Admin");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Delete Admin Board");

        var result = await _service.DeleteAsync(whiteboard.Id, admin.Id, true);

        Assert.True(result.Succeeded);
        Assert.Null(await _context.Whiteboards.FindAsync(whiteboard.Id));
    }

    [Fact]
    public async Task DeleteAsync_AsNonOwnerNonAdmin_Fails()
    {
        var owner = CreateUser("non-owner@test.com", "Owner");
        var user = CreateUser("user@test.com", "User");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Protected Board");

        var result = await _service.DeleteAsync(whiteboard.Id, user.Id, false);

        Assert.False(result.Succeeded);
        Assert.NotNull(await _context.Whiteboards.FindAsync(whiteboard.Id));
    }

    [Fact]
    public async Task SaveCanvasAsync_AsPresenter_UpdatesCanvasData()
    {
        var owner = CreateUser("presenter@test.com", "Presenter");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Canvas Board");

        var result = await _service.SaveCanvasAsync(whiteboard.Id, "{\"shapes\":[1]}", owner.Id);

        Assert.True(result.Succeeded);
        var updated = await _context.Whiteboards.FindAsync(whiteboard.Id);
        Assert.Equal("{\"shapes\":[1]}", updated!.CanvasData);
    }

    [Fact]
    public async Task SaveCanvasAsync_AsNonPresenter_Fails()
    {
        var owner = CreateUser("canvas-owner@test.com", "Owner");
        var viewer = CreateUser("viewer@test.com", "Viewer");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Canvas Board");

        var result = await _service.SaveCanvasAsync(whiteboard.Id, "{}", viewer.Id);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("not-json")]
    public async Task SaveCanvasAsync_WithUnsafeOrInvalidCanvasData_Fails(string canvasData)
    {
        var owner = CreateUser("canvas-validation@test.com", "Owner");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Canvas Validation Board");

        var result = await _service.SaveCanvasAsync(whiteboard.Id, canvasData, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("canvas data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveCanvasAsync_WithLargeCanvasPayload_Fails()
    {
        var owner = CreateUser("canvas-large@test.com", "Owner");
        var whiteboard = await CreateWhiteboardAsync(owner.Id, "Large Canvas Board");
        var largeCanvasData = "{\"shapes\":[\"" + new string('a', 500_001) + "\"],\"viewport\":{\"x\":0,\"y\":0,\"zoom\":1}}";

        var result = await _service.SaveCanvasAsync(whiteboard.Id, largeCanvasData, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("maximum allowed size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CanAccessAsync_ReturnsExpectedValuesForOwnerInviteeProjectMemberAndAdmin()
    {
        var owner = CreateUser("access-owner@test.com", "Owner");
        var invitee = CreateUser("access-invitee@test.com", "Invitee");
        var projectMember = CreateUser("access-member@test.com", "Project Member");
        var outsider = CreateUser("access-outsider@test.com", "Outsider");
        var admin = CreateUser("access-admin@test.com", "Admin");
        var project = await CreateProjectAsync("Access Project");

        AddProjectMember(project.Id, owner.Id, ProjectRole.Owner);
        AddProjectMember(project.Id, projectMember.Id, ProjectRole.Member);

        var tempBoard = await CreateWhiteboardAsync(owner.Id, "Temp Board");
        var savedBoard = await CreateWhiteboardAsync(owner.Id, "Saved Board", project.Id);

        _context.WhiteboardInvitations.Add(new WhiteboardInvitation
        {
            WhiteboardId = tempBoard.Id,
            UserId = invitee.Id,
            InvitedByUserId = owner.Id,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var ownerAccess = await _service.CanAccessAsync(tempBoard.Id, owner.Id, false);
        var inviteeAccess = await _service.CanAccessAsync(tempBoard.Id, invitee.Id, false);
        var projectMemberAccess = await _service.CanAccessAsync(savedBoard.Id, projectMember.Id, false);
        var outsiderAccess = await _service.CanAccessAsync(savedBoard.Id, outsider.Id, false);
        var adminAccess = await _service.CanAccessAsync(savedBoard.Id, admin.Id, true);

        Assert.True(ownerAccess.Succeeded && ownerAccess.Data);
        Assert.True(inviteeAccess.Succeeded && inviteeAccess.Data);
        Assert.True(projectMemberAccess.Succeeded && projectMemberAccess.Data);
        Assert.True(outsiderAccess.Succeeded);
        Assert.False(outsiderAccess.Data);
        Assert.True(adminAccess.Succeeded && adminAccess.Data);
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

    private async Task<Whiteboard> CreateWhiteboardAsync(string ownerId, string title, int? projectId = null, DateTime? updatedAt = null)
    {
        var whiteboard = new Whiteboard
        {
            Title = title,
            OwnerId = ownerId,
            ProjectId = projectId,
            CurrentPresenterId = ownerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };

        _context.Whiteboards.Add(whiteboard);
        await _context.SaveChangesAsync();
        return whiteboard;
    }
}
