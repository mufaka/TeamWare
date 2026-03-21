using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Models;

public class AdminActivityLogEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ApplicationUser _adminUser;
    private readonly ApplicationUser _targetUser;
    private readonly Project _project;

    public AdminActivityLogEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _adminUser = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin User"
        };

        _targetUser = new ApplicationUser
        {
            UserName = "target@test.com",
            Email = "target@test.com",
            DisplayName = "Target User"
        };

        _context.Users.Add(_adminUser);
        _context.Users.Add(_targetUser);

        _project = new Project
        {
            Name = "Test Project"
        };

        _context.Projects.Add(_project);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CanCreateAdminActivityLog()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "LockAccount",
            TargetUserId = _targetUser.Id,
            Details = "User locked for policy violation"
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Equal("LockAccount", retrieved.Action);
        Assert.Equal(_adminUser.Id, retrieved.AdminUserId);
        Assert.Equal(_targetUser.Id, retrieved.TargetUserId);
        Assert.Equal("User locked for policy violation", retrieved.Details);
    }

    [Fact]
    public async Task AdminActivityLog_HasTimestamp()
    {
        var before = DateTime.UtcNow;

        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "ResetPassword"
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.True(retrieved.CreatedAt >= before);
    }

    [Fact]
    public async Task AdminActivityLog_TargetUserIdIsOptional()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "ViewDashboard"
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.TargetUserId);
    }

    [Fact]
    public async Task AdminActivityLog_TargetProjectIdIsOptional()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "PromoteToAdmin",
            TargetUserId = _targetUser.Id
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.TargetProjectId);
    }

    [Fact]
    public async Task AdminActivityLog_DetailsIsOptional()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "UnlockAccount",
            TargetUserId = _targetUser.Id
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Details);
    }

    [Fact]
    public async Task AdminActivityLog_NavigationToAdminUser()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "LockAccount"
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs
            .Include(a => a.AdminUser)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.AdminUser);
        Assert.Equal(_adminUser.DisplayName, retrieved.AdminUser.DisplayName);
    }

    [Fact]
    public async Task AdminActivityLog_NavigationToTargetUser()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "ResetPassword",
            TargetUserId = _targetUser.Id
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs
            .Include(a => a.TargetUser)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.TargetUser);
        Assert.Equal(_targetUser.DisplayName, retrieved.TargetUser.DisplayName);
    }

    [Fact]
    public async Task AdminActivityLog_NavigationToTargetProject()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "EditProject",
            TargetProjectId = _project.Id
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs
            .Include(a => a.TargetProject)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.TargetProject);
        Assert.Equal(_project.Name, retrieved.TargetProject.Name);
    }

    [Fact]
    public async Task AdminActivityLog_CanHaveBothTargetUserAndProject()
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = "EditProject",
            TargetUserId = _targetUser.Id,
            TargetProjectId = _project.Id,
            Details = "Admin modified project membership"
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs
            .Include(a => a.AdminUser)
            .Include(a => a.TargetUser)
            .Include(a => a.TargetProject)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.AdminUser);
        Assert.NotNull(retrieved.TargetUser);
        Assert.NotNull(retrieved.TargetProject);
    }

    [Theory]
    [InlineData("LockAccount")]
    [InlineData("UnlockAccount")]
    [InlineData("ResetPassword")]
    [InlineData("PromoteToAdmin")]
    [InlineData("DemoteToUser")]
    [InlineData("EditProject")]
    public async Task AdminActivityLog_CanHaveVariousActionTypes(string action)
    {
        var log = new AdminActivityLog
        {
            AdminUserId = _adminUser.Id,
            Action = action
        };

        _context.AdminActivityLogs.Add(log);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(a => a.Action == action);

        Assert.NotNull(retrieved);
        Assert.Equal(action, retrieved.Action);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
