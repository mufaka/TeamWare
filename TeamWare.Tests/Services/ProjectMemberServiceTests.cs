using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class ProjectMemberServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectMemberService _memberService;
    private readonly ProjectService _projectService;

    public ProjectMemberServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _memberService = new ProjectMemberService(_context);
        _projectService = new ProjectService(_context);
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

    private async Task<Project> CreateProjectWithOwner(ApplicationUser owner, string name = "Test Project")
    {
        var result = await _projectService.CreateProject(name, null, owner.Id);
        return result.Data!;
    }

    // --- InviteMember ---

    [Fact]
    public async Task InviteMember_AsOwner_Success()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var newUser = CreateUser("new@test.com", "New User");
        var project = await CreateProjectWithOwner(owner);

        var result = await _memberService.InviteMember(project.Id, newUser.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(ProjectRole.Member, result.Data.Role);
    }

    [Fact]
    public async Task InviteMember_AsAdmin_Success()
    {
        var owner = CreateUser("owner2@test.com", "Owner");
        var admin = CreateUser("admin@test.com", "Admin");
        var newUser = CreateUser("new2@test.com", "New User");
        var project = await CreateProjectWithOwner(owner);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        var result = await _memberService.InviteMember(project.Id, newUser.Id, admin.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task InviteMember_AsMember_ReturnsFailure()
    {
        var owner = CreateUser("owner3@test.com", "Owner");
        var member = CreateUser("member@test.com", "Member");
        var newUser = CreateUser("new3@test.com", "New User");
        var project = await CreateProjectWithOwner(owner);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = member.Id,
            Role = ProjectRole.Member
        });
        await _context.SaveChangesAsync();

        var result = await _memberService.InviteMember(project.Id, newUser.Id, member.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("owners and admins"));
    }

    [Fact]
    public async Task InviteMember_AlreadyMember_ReturnsFailure()
    {
        var owner = CreateUser("owner4@test.com", "Owner");
        var existing = CreateUser("existing@test.com", "Existing");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, existing.Id, owner.Id);

        var result = await _memberService.InviteMember(project.Id, existing.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("already a member"));
    }

    [Fact]
    public async Task InviteMember_UserNotFound_ReturnsFailure()
    {
        var owner = CreateUser("owner5@test.com", "Owner");
        var project = await CreateProjectWithOwner(owner);

        var result = await _memberService.InviteMember(project.Id, "nonexistent-id", owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("User not found"));
    }

    [Fact]
    public async Task InviteMember_ProjectNotFound_ReturnsFailure()
    {
        var owner = CreateUser("owner6@test.com", "Owner");
        var newUser = CreateUser("new6@test.com", "New");

        var result = await _memberService.InviteMember(999, newUser.Id, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Project not found"));
    }

    // --- RemoveMember ---

    [Fact]
    public async Task RemoveMember_AsOwner_Success()
    {
        var owner = CreateUser("rm-owner@test.com", "Owner");
        var member = CreateUser("rm-member@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var result = await _memberService.RemoveMember(project.Id, member.Id, owner.Id);

        Assert.True(result.Succeeded);

        var remaining = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == project.Id && pm.UserId == member.Id)
            .FirstOrDefaultAsync();

        Assert.Null(remaining);
    }

    [Fact]
    public async Task RemoveMember_AsAdmin_Success()
    {
        var owner = CreateUser("rm-owner2@test.com", "Owner");
        var admin = CreateUser("rm-admin@test.com", "Admin");
        var member = CreateUser("rm-member2@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var result = await _memberService.RemoveMember(project.Id, member.Id, admin.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RemoveMember_CannotRemoveOwner()
    {
        var owner = CreateUser("rm-owner3@test.com", "Owner");
        var admin = CreateUser("rm-admin2@test.com", "Admin");
        var project = await CreateProjectWithOwner(owner);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        var result = await _memberService.RemoveMember(project.Id, owner.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Cannot remove the project owner"));
    }

    [Fact]
    public async Task RemoveMember_AsMember_ReturnsFailure()
    {
        var owner = CreateUser("rm-owner4@test.com", "Owner");
        var member1 = CreateUser("rm-m1@test.com", "Member 1");
        var member2 = CreateUser("rm-m2@test.com", "Member 2");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, member1.Id, owner.Id);
        await _memberService.InviteMember(project.Id, member2.Id, owner.Id);

        var result = await _memberService.RemoveMember(project.Id, member2.Id, member1.Id);

        Assert.False(result.Succeeded);
    }

    // --- UpdateMemberRole ---

    [Fact]
    public async Task UpdateMemberRole_AsOwner_Success()
    {
        var owner = CreateUser("role-owner@test.com", "Owner");
        var member = CreateUser("role-member@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var result = await _memberService.UpdateMemberRole(project.Id, member.Id, ProjectRole.Admin, owner.Id);

        Assert.True(result.Succeeded);

        var updated = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == project.Id && pm.UserId == member.Id);

        Assert.Equal(ProjectRole.Admin, updated!.Role);
    }

    [Fact]
    public async Task UpdateMemberRole_AsAdmin_ReturnsFailure()
    {
        var owner = CreateUser("role-owner2@test.com", "Owner");
        var admin = CreateUser("role-admin@test.com", "Admin");
        var member = CreateUser("role-member2@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = admin.Id,
            Role = ProjectRole.Admin
        });
        await _context.SaveChangesAsync();

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var result = await _memberService.UpdateMemberRole(project.Id, member.Id, ProjectRole.Admin, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Only project owners"));
    }

    [Fact]
    public async Task UpdateMemberRole_CannotAssignOwnerRole()
    {
        var owner = CreateUser("role-owner3@test.com", "Owner");
        var member = CreateUser("role-member3@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var result = await _memberService.UpdateMemberRole(project.Id, member.Id, ProjectRole.Owner, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Cannot assign the Owner role"));
    }

    [Fact]
    public async Task UpdateMemberRole_CannotChangeOwnRole()
    {
        var owner = CreateUser("role-owner4@test.com", "Owner");
        var project = await CreateProjectWithOwner(owner);

        var result = await _memberService.UpdateMemberRole(project.Id, owner.Id, ProjectRole.Admin, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Cannot change your own role"));
    }

    // --- GetMembers ---

    [Fact]
    public async Task GetMembers_AsMember_ReturnsAllMembers()
    {
        var owner = CreateUser("gm-owner@test.com", "Owner");
        var member = CreateUser("gm-member@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var result = await _memberService.GetMembers(project.Id, owner.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetMembers_NonMember_ReturnsFailure()
    {
        var owner = CreateUser("gm-owner2@test.com", "Owner");
        var outsider = CreateUser("gm-outsider@test.com", "Outsider");
        var project = await CreateProjectWithOwner(owner);

        var result = await _memberService.GetMembers(project.Id, outsider.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not a member"));
    }

    // --- GetMemberUserIds ---

    [Fact]
    public async Task GetMemberUserIds_ReturnsAllMemberIds()
    {
        var owner = CreateUser("gmuid-owner@test.com", "Owner");
        var member = CreateUser("gmuid-member@test.com", "Member");
        var project = await CreateProjectWithOwner(owner);

        await _memberService.InviteMember(project.Id, member.Id, owner.Id);

        var ids = await _memberService.GetMemberUserIds(project.Id);

        Assert.Equal(2, ids.Count);
        Assert.Contains(owner.Id, ids);
        Assert.Contains(member.Id, ids);
    }

    [Fact]
    public async Task GetMemberUserIds_NoMembers_ReturnsEmpty()
    {
        var ids = await _memberService.GetMemberUserIds(999);

        Assert.Empty(ids);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
