using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class ProjectMemberEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ProjectMemberEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task CanCreateProjectMember()
    {
        var user = new ApplicationUser
        {
            UserName = "member@test.com",
            Email = "member@test.com",
            DisplayName = "Test Member"
        };
        _context.Users.Add(user);

        var project = new Project { Name = "Test Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var member = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Owner
        };

        _context.ProjectMembers.Add(member);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ProjectMembers
            .Include(pm => pm.Project)
            .Include(pm => pm.User)
            .FirstOrDefaultAsync(pm => pm.ProjectId == project.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(ProjectRole.Owner, retrieved.Role);
        Assert.Equal("Test Project", retrieved.Project.Name);
        Assert.Equal("Test Member", retrieved.User.DisplayName);
    }

    [Fact]
    public async Task ProjectMember_DefaultRole_IsMember()
    {
        var user = new ApplicationUser
        {
            UserName = "default@test.com",
            Email = "default@test.com",
            DisplayName = "Default Role User"
        };
        _context.Users.Add(user);

        var project = new Project { Name = "Default Role Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var member = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id
        };

        _context.ProjectMembers.Add(member);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ProjectMembers.FirstOrDefaultAsync(pm => pm.UserId == user.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(ProjectRole.Member, retrieved.Role);
    }

    [Fact]
    public async Task ProjectMember_UniqueConstraint_ProjectAndUser()
    {
        var user = new ApplicationUser
        {
            UserName = "unique@test.com",
            Email = "unique@test.com",
            DisplayName = "Unique Test"
        };
        _context.Users.Add(user);

        var project = new Project { Name = "Unique Constraint Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Owner
        });
        await _context.SaveChangesAsync();

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Member
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProjectMember_CascadeDelete_WhenProjectDeleted()
    {
        var user = new ApplicationUser
        {
            UserName = "cascade@test.com",
            Email = "cascade@test.com",
            DisplayName = "Cascade Test"
        };
        _context.Users.Add(user);

        var project = new Project { Name = "Cascade Delete Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Owner
        });
        await _context.SaveChangesAsync();

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        var members = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == project.Id)
            .ToListAsync();

        Assert.Empty(members);
    }

    [Fact]
    public async Task Project_CanHaveMultipleMembers()
    {
        var user1 = new ApplicationUser
        {
            UserName = "user1@test.com",
            Email = "user1@test.com",
            DisplayName = "User One"
        };
        var user2 = new ApplicationUser
        {
            UserName = "user2@test.com",
            Email = "user2@test.com",
            DisplayName = "User Two"
        };
        _context.Users.AddRange(user1, user2);

        var project = new Project { Name = "Multi Member Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = project.Id, UserId = user1.Id, Role = ProjectRole.Owner },
            new ProjectMember { ProjectId = project.Id, UserId = user2.Id, Role = ProjectRole.Member }
        );
        await _context.SaveChangesAsync();

        var retrieved = await _context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == project.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Members.Count);
    }

    [Theory]
    [InlineData(ProjectRole.Owner)]
    [InlineData(ProjectRole.Admin)]
    [InlineData(ProjectRole.Member)]
    public async Task ProjectMember_CanHaveAllRoles(ProjectRole role)
    {
        var user = new ApplicationUser
        {
            UserName = $"role-{role}@test.com",
            Email = $"role-{role}@test.com",
            DisplayName = $"Role {role} User"
        };
        _context.Users.Add(user);

        var project = new Project { Name = $"Role {role} Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            Role = role
        });
        await _context.SaveChangesAsync();

        var retrieved = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.UserId == user.Id && pm.ProjectId == project.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(role, retrieved.Role);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
