using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class ProjectInvitationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ProjectInvitationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
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

    private Project CreateProject(string name = "Test Project")
    {
        var project = new Project
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Projects.Add(project);
        _context.SaveChanges();
        return project;
    }

    [Fact]
    public void ProjectInvitation_DefaultStatus_IsPending()
    {
        var invitation = new ProjectInvitation();

        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public void ProjectInvitation_DefaultRole_IsMember()
    {
        var invitation = new ProjectInvitation();

        Assert.Equal(ProjectRole.Member, invitation.Role);
    }

    [Fact]
    public void ProjectInvitation_DefaultRespondedAt_IsNull()
    {
        var invitation = new ProjectInvitation();

        Assert.Null(invitation.RespondedAt);
    }

    [Fact]
    public async Task ProjectInvitation_CanBePersisted()
    {
        var inviter = CreateUser("inviter@test.com", "Inviter");
        var invitee = CreateUser("invitee@test.com", "Invitee");
        var project = CreateProject();

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = invitee.Id,
            InvitedByUserId = inviter.Id,
            Status = InvitationStatus.Pending,
            Role = ProjectRole.Member,
            CreatedAt = DateTime.UtcNow
        };

        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var saved = await _context.ProjectInvitations.FindAsync(invitation.Id);
        Assert.NotNull(saved);
        Assert.Equal(project.Id, saved.ProjectId);
        Assert.Equal(invitee.Id, saved.InvitedUserId);
        Assert.Equal(inviter.Id, saved.InvitedByUserId);
        Assert.Equal(InvitationStatus.Pending, saved.Status);
        Assert.Equal(ProjectRole.Member, saved.Role);
    }

    [Fact]
    public async Task ProjectInvitation_NavigationProperties_AreLoaded()
    {
        var inviter = CreateUser("nav-inviter@test.com", "Nav Inviter");
        var invitee = CreateUser("nav-invitee@test.com", "Nav Invitee");
        var project = CreateProject("Nav Project");

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = invitee.Id,
            InvitedByUserId = inviter.Id
        };

        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var saved = await _context.ProjectInvitations
            .Include(i => i.Project)
            .Include(i => i.InvitedUser)
            .Include(i => i.InvitedByUser)
            .FirstAsync(i => i.Id == invitation.Id);

        Assert.Equal("Nav Project", saved.Project.Name);
        Assert.Equal("Nav Invitee", saved.InvitedUser.DisplayName);
        Assert.Equal("Nav Inviter", saved.InvitedByUser.DisplayName);
    }

    [Fact]
    public async Task ProjectInvitation_StatusStoredAsString()
    {
        var inviter = CreateUser("str-inviter@test.com", "Str Inviter");
        var invitee = CreateUser("str-invitee@test.com", "Str Invitee");
        var project = CreateProject("Str Project");

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = invitee.Id,
            InvitedByUserId = inviter.Id,
            Status = InvitationStatus.Accepted
        };

        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        // Query raw to verify string storage
        var statusValue = await _context.Database
            .SqlQueryRaw<string>($"SELECT Status AS Value FROM ProjectInvitations WHERE Id = {invitation.Id}")
            .FirstAsync();

        Assert.Equal("Accepted", statusValue);
    }

    [Fact]
    public async Task ProjectInvitation_RoleStoredAsString()
    {
        var inviter = CreateUser("role-inviter@test.com", "Role Inviter");
        var invitee = CreateUser("role-invitee@test.com", "Role Invitee");
        var project = CreateProject("Role Project");

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = invitee.Id,
            InvitedByUserId = inviter.Id,
            Role = ProjectRole.Admin
        };

        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var roleValue = await _context.Database
            .SqlQueryRaw<string>($"SELECT Role AS Value FROM ProjectInvitations WHERE Id = {invitation.Id}")
            .FirstAsync();

        Assert.Equal("Admin", roleValue);
    }

    [Fact]
    public async Task ProjectInvitation_CascadeDeleteWithProject()
    {
        var inviter = CreateUser("casc-inviter@test.com", "Cascade Inviter");
        var invitee = CreateUser("casc-invitee@test.com", "Cascade Invitee");
        var project = CreateProject("Cascade Project");

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = invitee.Id,
            InvitedByUserId = inviter.Id
        };

        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        var count = await _context.ProjectInvitations.CountAsync(i => i.Id == invitation.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ProjectInvitation_RespondedAt_CanBeSet()
    {
        var inviter = CreateUser("resp-inviter@test.com", "Resp Inviter");
        var invitee = CreateUser("resp-invitee@test.com", "Resp Invitee");
        var project = CreateProject("Resp Project");

        var respondedAt = DateTime.UtcNow;

        var invitation = new ProjectInvitation
        {
            ProjectId = project.Id,
            InvitedUserId = invitee.Id,
            InvitedByUserId = inviter.Id,
            Status = InvitationStatus.Declined,
            RespondedAt = respondedAt
        };

        _context.ProjectInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var saved = await _context.ProjectInvitations.FindAsync(invitation.Id);
        Assert.NotNull(saved);
        Assert.Equal(InvitationStatus.Declined, saved.Status);
        Assert.NotNull(saved.RespondedAt);
    }

    [Fact]
    public async Task InvitationStatus_AllValuesAreValid()
    {
        Assert.Equal(0, (int)InvitationStatus.Pending);
        Assert.Equal(1, (int)InvitationStatus.Accepted);
        Assert.Equal(2, (int)InvitationStatus.Declined);

        var values = Enum.GetValues<InvitationStatus>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public async Task NotificationType_IncludesProjectInvitation()
    {
        var values = Enum.GetValues<NotificationType>();
        Assert.Contains(NotificationType.ProjectInvitation, values);
    }

    [Fact]
    public async Task ProjectInvitations_DbSetExists()
    {
        var count = await _context.ProjectInvitations.CountAsync();
        Assert.True(count >= 0);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
