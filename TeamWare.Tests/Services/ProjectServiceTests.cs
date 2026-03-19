using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ProjectServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ProjectService(_context);
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

    // --- CreateProject ---

    [Fact]
    public async Task CreateProject_Success_ReturnsProject()
    {
        var user = CreateUser("creator@test.com", "Creator");

        var result = await _service.CreateProject("My Project", "A description", user.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("My Project", result.Data.Name);
        Assert.Equal("A description", result.Data.Description);
        Assert.Equal(ProjectStatus.Active, result.Data.Status);
    }

    [Fact]
    public async Task CreateProject_AutoAssignsCreatorAsOwner()
    {
        var user = CreateUser("owner@test.com", "Owner");

        var result = await _service.CreateProject("Owned Project", null, user.Id);

        Assert.True(result.Succeeded);

        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == result.Data!.Id && pm.UserId == user.Id);

        Assert.NotNull(membership);
        Assert.Equal(ProjectRole.Owner, membership.Role);
    }

    [Fact]
    public async Task CreateProject_EmptyName_ReturnsFailure()
    {
        var user = CreateUser("fail@test.com", "Fail User");

        var result = await _service.CreateProject("", null, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("name is required"));
    }

    [Fact]
    public async Task CreateProject_WhitespaceName_ReturnsFailure()
    {
        var user = CreateUser("ws@test.com", "WS User");

        var result = await _service.CreateProject("   ", null, user.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateProject_TrimsName()
    {
        var user = CreateUser("trim@test.com", "Trim User");

        var result = await _service.CreateProject("  Trimmed Project  ", null, user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Trimmed Project", result.Data!.Name);
    }

    // --- UpdateProject ---

    [Fact]
    public async Task UpdateProject_AsOwner_Success()
    {
        var user = CreateUser("updater@test.com", "Updater");
        var createResult = await _service.CreateProject("Original", "Original desc", user.Id);

        var result = await _service.UpdateProject(createResult.Data!.Id, "Updated", "Updated desc", user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.Data!.Name);
        Assert.Equal("Updated desc", result.Data.Description);
    }

    [Fact]
    public async Task UpdateProject_AsAdmin_Success()
    {
        var owner = CreateUser("owner2@test.com", "Owner");
        var admin = CreateUser("admin@test.com", "Admin");

        var createResult = await _service.CreateProject("Admin Edit Project", null, owner.Id);
        var projectId = createResult.Data!.Id;

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        var result = await _service.UpdateProject(projectId, "Admin Updated", "By admin", admin.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Admin Updated", result.Data!.Name);
    }

    [Fact]
    public async Task UpdateProject_AsMember_ReturnsFailure()
    {
        var owner = CreateUser("owner3@test.com", "Owner");
        var member = CreateUser("member@test.com", "Member");

        var createResult = await _service.CreateProject("Member Edit Project", null, owner.Id);
        var projectId = createResult.Data!.Id;

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = member.Id,
            Role = ProjectRole.Member
        });
        await _context.SaveChangesAsync();

        var result = await _service.UpdateProject(projectId, "Should Fail", null, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("owners and admins"));
    }

    [Fact]
    public async Task UpdateProject_NonMember_ReturnsFailure()
    {
        var owner = CreateUser("owner4@test.com", "Owner");
        var outsider = CreateUser("outsider@test.com", "Outsider");

        var createResult = await _service.CreateProject("Outsider Edit Project", null, owner.Id);

        var result = await _service.UpdateProject(createResult.Data!.Id, "Should Fail", null, outsider.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateProject_NotFound_ReturnsFailure()
    {
        var user = CreateUser("notfound@test.com", "NotFound");

        var result = await _service.UpdateProject(999, "Nothing", null, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    // --- ArchiveProject ---

    [Fact]
    public async Task ArchiveProject_AsOwner_Success()
    {
        var owner = CreateUser("archive-owner@test.com", "Owner");
        var createResult = await _service.CreateProject("Archive Me", null, owner.Id);

        var result = await _service.ArchiveProject(createResult.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);

        var project = await _context.Projects.FindAsync(createResult.Data!.Id);
        Assert.Equal(ProjectStatus.Archived, project!.Status);
    }

    [Fact]
    public async Task ArchiveProject_AsAdmin_ReturnsFailure()
    {
        var owner = CreateUser("archive-owner2@test.com", "Owner");
        var admin = CreateUser("archive-admin@test.com", "Admin");

        var createResult = await _service.CreateProject("No Admin Archive", null, owner.Id);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = createResult.Data!.Id,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        var result = await _service.ArchiveProject(createResult.Data.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("owners"));
    }

    // --- DeleteProject ---

    [Fact]
    public async Task DeleteProject_AsOwner_Success()
    {
        var owner = CreateUser("delete-owner@test.com", "Owner");
        var createResult = await _service.CreateProject("Delete Me", null, owner.Id);

        var result = await _service.DeleteProject(createResult.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);

        var project = await _context.Projects.FindAsync(createResult.Data!.Id);
        Assert.Null(project);
    }

    [Fact]
    public async Task DeleteProject_AsAdmin_ReturnsFailure()
    {
        var owner = CreateUser("delete-owner2@test.com", "Owner");
        var admin = CreateUser("delete-admin@test.com", "Admin");

        var createResult = await _service.CreateProject("No Admin Delete", null, owner.Id);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = createResult.Data!.Id,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        var result = await _service.DeleteProject(createResult.Data.Id, admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- GetProjectsForUser ---

    [Fact]
    public async Task GetProjectsForUser_ReturnsUserProjects()
    {
        var user = CreateUser("list@test.com", "List User");

        await _service.CreateProject("Project A", null, user.Id);
        await _service.CreateProject("Project B", null, user.Id);

        var result = await _service.GetProjectsForUser(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetProjectsForUser_DoesNotReturnOtherUsersProjects()
    {
        var user1 = CreateUser("user1@test.com", "User One");
        var user2 = CreateUser("user2@test.com", "User Two");

        await _service.CreateProject("User1 Project", null, user1.Id);
        await _service.CreateProject("User2 Project", null, user2.Id);

        var result = await _service.GetProjectsForUser(user1.Id);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("User1 Project", result.Data![0].Name);
    }

    [Fact]
    public async Task GetProjectsForUser_ReturnsEmptyForUserWithNoProjects()
    {
        var user = CreateUser("empty@test.com", "Empty User");

        var result = await _service.GetProjectsForUser(user.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    // --- GetProjectDashboard ---

    [Fact]
    public async Task GetProjectDashboard_AsMember_Success()
    {
        var owner = CreateUser("dash-owner@test.com", "Owner");
        var createResult = await _service.CreateProject("Dashboard Project", "Desc", owner.Id);

        var result = await _service.GetProjectDashboard(createResult.Data!.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Dashboard Project", result.Data.Project.Name);
        Assert.Equal(1, result.Data.TotalMembers);
        Assert.Equal(0, result.Data.TotalTasks);
    }

    [Fact]
    public async Task GetProjectDashboard_NonMember_ReturnsFailure()
    {
        var owner = CreateUser("dash-owner2@test.com", "Owner");
        var outsider = CreateUser("dash-outsider@test.com", "Outsider");

        var createResult = await _service.CreateProject("Private Dashboard", null, owner.Id);

        var result = await _service.GetProjectDashboard(createResult.Data!.Id, outsider.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not a member"));
    }

    [Fact]
    public async Task GetProjectDashboard_NotFound_ReturnsFailure()
    {
        var user = CreateUser("dash-nf@test.com", "NotFound");

        var result = await _service.GetProjectDashboard(999, user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
