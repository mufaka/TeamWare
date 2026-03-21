using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class AdminActivityLogServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly AdminActivityLogService _service;
    private readonly ApplicationUser _adminUser;
    private readonly ApplicationUser _targetUser;
    private readonly Project _project;

    public AdminActivityLogServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new AdminActivityLogService(_context);

        _adminUser = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin"
        };
        _targetUser = new ApplicationUser
        {
            UserName = "target@test.com",
            Email = "target@test.com",
            DisplayName = "Target"
        };
        _context.Users.Add(_adminUser);
        _context.Users.Add(_targetUser);

        _project = new Project { Name = "Test Project" };
        _context.Projects.Add(_project);
        _context.SaveChanges();
    }

    [Fact]
    public async Task LogAction_CreatesEntry()
    {
        await _service.LogAction(_adminUser.Id, "LockAccount", _targetUser.Id);

        var entry = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.Equal(_adminUser.Id, entry.AdminUserId);
        Assert.Equal("LockAccount", entry.Action);
        Assert.Equal(_targetUser.Id, entry.TargetUserId);
    }

    [Fact]
    public async Task LogAction_WithDetails()
    {
        await _service.LogAction(_adminUser.Id, "ResetPassword", _targetUser.Id, details: "Password was reset");

        var entry = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.Equal("Password was reset", entry.Details);
    }

    [Fact]
    public async Task LogAction_WithTargetProject()
    {
        await _service.LogAction(_adminUser.Id, "EditProject", targetProjectId: _project.Id);

        var entry = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.Equal(_project.Id, entry.TargetProjectId);
    }

    [Fact]
    public async Task LogAction_SetsTimestamp()
    {
        var before = DateTime.UtcNow;

        await _service.LogAction(_adminUser.Id, "LockAccount");

        var entry = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(entry);
        Assert.True(entry.CreatedAt >= before);
    }

    [Fact]
    public async Task GetActivityLog_ReturnsPaginatedResults()
    {
        for (int i = 0; i < 15; i++)
        {
            await _service.LogAction(_adminUser.Id, $"Action{i}");
        }

        var result = await _service.GetActivityLog(1, 10);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(10, result.Data.Items.Count);
        Assert.Equal(15, result.Data.TotalCount);
        Assert.Equal(2, result.Data.TotalPages);
    }

    [Fact]
    public async Task GetActivityLog_SecondPage()
    {
        for (int i = 0; i < 15; i++)
        {
            await _service.LogAction(_adminUser.Id, $"Action{i}");
        }

        var result = await _service.GetActivityLog(2, 10);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.Items.Count);
    }

    [Fact]
    public async Task GetActivityLog_OrderedByCreatedAtDescending()
    {
        await _service.LogAction(_adminUser.Id, "First");
        await _service.LogAction(_adminUser.Id, "Second");
        await _service.LogAction(_adminUser.Id, "Third");

        var result = await _service.GetActivityLog(1, 10);

        Assert.True(result.Succeeded);
        var items = result.Data!.Items;
        Assert.Equal("Third", items[0].Action);
        Assert.Equal("Second", items[1].Action);
        Assert.Equal("First", items[2].Action);
    }

    [Fact]
    public async Task GetActivityLog_IncludesNavigationProperties()
    {
        await _service.LogAction(_adminUser.Id, "LockAccount", _targetUser.Id, _project.Id);

        var result = await _service.GetActivityLog(1, 10);

        Assert.True(result.Succeeded);
        var entry = result.Data!.Items[0];
        Assert.NotNull(entry.AdminUser);
        Assert.NotNull(entry.TargetUser);
        Assert.NotNull(entry.TargetProject);
    }

    [Fact]
    public async Task GetActivityLog_InvalidPage_ReturnsFailure()
    {
        var result = await _service.GetActivityLog(0, 10);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetActivityLog_InvalidPageSize_ReturnsFailure()
    {
        var result = await _service.GetActivityLog(1, 0);

        Assert.False(result.Succeeded);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
