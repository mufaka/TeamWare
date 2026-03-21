using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class AdminServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly AdminService _service;
    private readonly AdminActivityLogService _activityLogService;

    public AdminServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(_connection));

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        _context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _context.Database.EnsureCreated();

        _userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        roleManager.CreateAsync(new IdentityRole(SeedData.AdminRoleName)).GetAwaiter().GetResult();
        roleManager.CreateAsync(new IdentityRole(SeedData.UserRoleName)).GetAwaiter().GetResult();

        _activityLogService = new AdminActivityLogService(_context);
        _service = new AdminService(_context, _userManager, _activityLogService);
    }

    private async Task<ApplicationUser> CreateUser(string email, string displayName, bool isAdmin = false)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };

        await _userManager.CreateAsync(user, "TestPass1");
        await _userManager.AddToRoleAsync(user, SeedData.UserRoleName);

        if (isAdmin)
        {
            await _userManager.AddToRoleAsync(user, SeedData.AdminRoleName);
        }

        return user;
    }

    // --- GetAllUsers ---

    [Fact]
    public async Task GetAllUsers_ReturnsAllUsers()
    {
        await CreateUser("user1@test.com", "User One");
        await CreateUser("user2@test.com", "User Two");

        var result = await _service.GetAllUsers(null, 1, 10);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
    }

    [Fact]
    public async Task GetAllUsers_SearchByDisplayName()
    {
        await CreateUser("alice@test.com", "Alice Smith");
        await CreateUser("bob@test.com", "Bob Jones");

        var result = await _service.GetAllUsers("Alice", 1, 10);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal("Alice Smith", result.Data.Items[0].DisplayName);
    }

    [Fact]
    public async Task GetAllUsers_SearchByEmail()
    {
        await CreateUser("alice@test.com", "Alice Smith");
        await CreateUser("bob@test.com", "Bob Jones");

        var result = await _service.GetAllUsers("bob@", 1, 10);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalCount);
    }

    [Fact]
    public async Task GetAllUsers_SearchIsCaseInsensitive()
    {
        await CreateUser("alice@test.com", "Alice Smith");

        var result = await _service.GetAllUsers("alice", 1, 10);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalCount);
    }

    [Fact]
    public async Task GetAllUsers_Pagination()
    {
        for (int i = 0; i < 15; i++)
        {
            await CreateUser($"user{i}@test.com", $"User {i:D2}");
        }

        var page1 = await _service.GetAllUsers(null, 1, 10);
        var page2 = await _service.GetAllUsers(null, 2, 10);

        Assert.Equal(10, page1.Data!.Items.Count);
        Assert.Equal(5, page2.Data!.Items.Count);
        Assert.Equal(15, page1.Data.TotalCount);
    }

    [Fact]
    public async Task GetAllUsers_InvalidPage_ReturnsFailure()
    {
        var result = await _service.GetAllUsers(null, 0, 10);

        Assert.False(result.Succeeded);
    }

    // --- LockUser ---

    [Fact]
    public async Task LockUser_Success()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        var result = await _service.LockUser(target.Id, admin.Id);

        Assert.True(result.Succeeded);

        var updated = await _userManager.FindByIdAsync(target.Id);
        Assert.NotNull(updated);
        Assert.True(updated.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LockUser_LogsAction()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        await _service.LockUser(target.Id, admin.Id);

        var log = await _context.AdminActivityLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("LockAccount", log.Action);
        Assert.Equal(admin.Id, log.AdminUserId);
        Assert.Equal(target.Id, log.TargetUserId);
    }

    [Fact]
    public async Task LockUser_CannotLockSelf()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.LockUser(admin.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot lock your own account", result.Errors[0]);
    }

    [Fact]
    public async Task LockUser_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.LockUser("nonexistent-id", admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- UnlockUser ---

    [Fact]
    public async Task UnlockUser_Success()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        await _service.LockUser(target.Id, admin.Id);
        var result = await _service.UnlockUser(target.Id, admin.Id);

        Assert.True(result.Succeeded);

        var updated = await _userManager.FindByIdAsync(target.Id);
        Assert.NotNull(updated);
        Assert.True(updated.LockoutEnd == null || updated.LockoutEnd <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task UnlockUser_LogsAction()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        await _service.UnlockUser(target.Id, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "UnlockAccount");
        Assert.NotNull(log);
    }

    [Fact]
    public async Task UnlockUser_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.UnlockUser("nonexistent-id", admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- ResetPassword ---

    [Fact]
    public async Task ResetPassword_Success()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        var result = await _service.ResetPassword(target.Id, "NewPass123", admin.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_LogsAction()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        await _service.ResetPassword(target.Id, "NewPass123", admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "ResetPassword");
        Assert.NotNull(log);
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        var result = await _service.ResetPassword(target.Id, "weak", admin.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.ResetPassword("nonexistent-id", "NewPass123", admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- PromoteToAdmin ---

    [Fact]
    public async Task PromoteToAdmin_Success()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        var result = await _service.PromoteToAdmin(target.Id, admin.Id);

        Assert.True(result.Succeeded);
        Assert.True(await _userManager.IsInRoleAsync(target, SeedData.AdminRoleName));
    }

    [Fact]
    public async Task PromoteToAdmin_LogsAction()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        await _service.PromoteToAdmin(target.Id, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "PromoteToAdmin");
        Assert.NotNull(log);
    }

    [Fact]
    public async Task PromoteToAdmin_AlreadyAdmin_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target", isAdmin: true);

        var result = await _service.PromoteToAdmin(target.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("already an admin", result.Errors[0]);
    }

    [Fact]
    public async Task PromoteToAdmin_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.PromoteToAdmin("nonexistent-id", admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- DemoteToUser ---

    [Fact]
    public async Task DemoteToUser_Success()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target", isAdmin: true);

        var result = await _service.DemoteToUser(target.Id, admin.Id);

        Assert.True(result.Succeeded);
        Assert.False(await _userManager.IsInRoleAsync(target, SeedData.AdminRoleName));
    }

    [Fact]
    public async Task DemoteToUser_LogsAction()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target", isAdmin: true);

        await _service.DemoteToUser(target.Id, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "DemoteToUser");
        Assert.NotNull(log);
    }

    [Fact]
    public async Task DemoteToUser_CannotDemoteSelf()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.DemoteToUser(admin.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot demote your own account", result.Errors[0]);
    }

    [Fact]
    public async Task DemoteToUser_NotAdmin_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);
        var target = await CreateUser("target@test.com", "Target");

        var result = await _service.DemoteToUser(target.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not an admin", result.Errors[0]);
    }

    [Fact]
    public async Task DemoteToUser_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateUser("admin@test.com", "Admin", isAdmin: true);

        var result = await _service.DemoteToUser("nonexistent-id", admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- GetSystemStatistics ---

    [Fact]
    public async Task GetSystemStatistics_ReturnsCorrectCounts()
    {
        var user = await CreateUser("user@test.com", "User");

        var project = new Project { Name = "P1" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.TaskItems.Add(new TaskItem
        {
            Title = "T1",
            ProjectId = project.Id,
            CreatedByUserId = user.Id
        });
        _context.TaskItems.Add(new TaskItem
        {
            Title = "T2",
            ProjectId = project.Id,
            CreatedByUserId = user.Id
        });
        await _context.SaveChangesAsync();

        var result = await _service.GetSystemStatistics();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalUsers);
        Assert.Equal(1, result.Data.TotalProjects);
        Assert.Equal(2, result.Data.TotalTasks);
    }

    [Fact]
    public async Task GetSystemStatistics_EmptyDatabase()
    {
        var result = await _service.GetSystemStatistics();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data!.TotalUsers);
        Assert.Equal(0, result.Data.TotalProjects);
        Assert.Equal(0, result.Data.TotalTasks);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
