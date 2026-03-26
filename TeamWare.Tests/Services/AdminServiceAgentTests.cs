using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class AdminServiceAgentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly AdminService _service;
    private readonly AdminActivityLogService _activityLogService;
    private readonly PersonalAccessTokenService _tokenService;

    public AdminServiceAgentTests()
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
        _tokenService = new PersonalAccessTokenService(_context);
        _service = new AdminService(_context, _userManager, _activityLogService, _tokenService);
    }

    private async Task<ApplicationUser> CreateHumanUser(string email, string displayName)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName
        };

        await _userManager.CreateAsync(user, "TestPass1");
        await _userManager.AddToRoleAsync(user, SeedData.UserRoleName);
        return user;
    }

    private async Task<ApplicationUser> CreateAdminUser()
    {
        var user = new ApplicationUser
        {
            UserName = "admin@test.com",
            Email = "admin@test.com",
            DisplayName = "Admin"
        };

        await _userManager.CreateAsync(user, "TestPass1");
        await _userManager.AddToRoleAsync(user, SeedData.UserRoleName);
        await _userManager.AddToRoleAsync(user, SeedData.AdminRoleName);
        return user;
    }

    // --- CreateAgentUser ---

    [Fact]
    public async Task CreateAgentUser_Success_CreatesUserWithIsAgentTrue()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("CodeBot", "An automated coding agent", admin.Id);

        Assert.True(result.Succeeded);

        var (user, _) = result.Data!;
        Assert.True(user.IsAgent);
        Assert.True(user.IsAgentActive);
        Assert.Equal("CodeBot", user.DisplayName);
        Assert.Equal("An automated coding agent", user.AgentDescription);
    }

    [Fact]
    public async Task CreateAgentUser_GeneratesUsernameFromDisplayName()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("Code Bot", null, admin.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("agent-code-bot", result.Data!.User.UserName);
    }

    [Fact]
    public async Task CreateAgentUser_GeneratesPlaceholderEmail()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("Code Bot", null, admin.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("agent-code-bot@agent.local", result.Data!.User.Email);
    }

    [Fact]
    public async Task CreateAgentUser_GeneratesPAT()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("CodeBot", null, admin.Id);

        Assert.True(result.Succeeded);

        var rawToken = result.Data!.RawToken;
        Assert.False(string.IsNullOrEmpty(rawToken));
        Assert.StartsWith("tw_", rawToken);
    }

    [Fact]
    public async Task CreateAgentUser_DoesNotAssignAdminRole()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("CodeBot", null, admin.Id);

        Assert.True(result.Succeeded);
        Assert.False(await _userManager.IsInRoleAsync(result.Data!.User, SeedData.AdminRoleName));
    }

    [Fact]
    public async Task CreateAgentUser_AssignsUserRole()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("CodeBot", null, admin.Id);

        Assert.True(result.Succeeded);
        Assert.True(await _userManager.IsInRoleAsync(result.Data!.User, SeedData.UserRoleName));
    }

    [Fact]
    public async Task CreateAgentUser_LogsAction()
    {
        var admin = await CreateAdminUser();
        await _service.CreateAgentUser("CodeBot", null, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "CreateAgentUser");
        Assert.NotNull(log);
    }

    [Fact]
    public async Task CreateAgentUser_EmptyDisplayName_ReturnsFailure()
    {
        var admin = await CreateAdminUser();
        var result = await _service.CreateAgentUser("", null, admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- UpdateAgentUser ---

    [Fact]
    public async Task UpdateAgentUser_Success()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", "Original description", admin.Id);
        var userId = createResult.Data!.User.Id;

        var result = await _service.UpdateAgentUser(userId, "Updated Bot", "New description", admin.Id);

        Assert.True(result.Succeeded);

        var user = await _userManager.FindByIdAsync(userId);
        Assert.Equal("Updated Bot", user!.DisplayName);
        Assert.Equal("New description", user.AgentDescription);
    }

    [Fact]
    public async Task UpdateAgentUser_NonAgent_ReturnsFailure()
    {
        var humanUser = await CreateHumanUser("human@test.com", "Human");

        var admin = await CreateAdminUser();
        var result = await _service.UpdateAgentUser(humanUser.Id, "New Name", null, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not an agent", result.Errors[0]);
    }

    [Fact]
    public async Task UpdateAgentUser_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateAdminUser();
        var result = await _service.UpdateAgentUser("nonexistent-id", "Name", null, admin.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAgentUser_LogsAction()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var userId = createResult.Data!.User.Id;

        await _service.UpdateAgentUser(userId, "Updated Bot", null, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "UpdateAgentUser");
        Assert.NotNull(log);
    }

    // --- SetAgentActive ---

    [Fact]
    public async Task SetAgentActive_PauseAgent_Success()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var userId = createResult.Data!.User.Id;

        var result = await _service.SetAgentActive(userId, false, admin.Id);

        Assert.True(result.Succeeded);

        var user = await _userManager.FindByIdAsync(userId);
        Assert.False(user!.IsAgentActive);
    }

    [Fact]
    public async Task SetAgentActive_ResumeAgent_Success()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var userId = createResult.Data!.User.Id;

        await _service.SetAgentActive(userId, false, admin.Id);
        var result = await _service.SetAgentActive(userId, true, admin.Id);

        Assert.True(result.Succeeded);

        var user = await _userManager.FindByIdAsync(userId);
        Assert.True(user!.IsAgentActive);
    }

    [Fact]
    public async Task SetAgentActive_NonAgent_ReturnsFailure()
    {
        var humanUser = await CreateHumanUser("human@test.com", "Human");

        var admin = await CreateAdminUser();
        var result = await _service.SetAgentActive(humanUser.Id, false, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not an agent", result.Errors[0]);
    }

    [Fact]
    public async Task SetAgentActive_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateAdminUser();
        var result = await _service.SetAgentActive("nonexistent-id", false, admin.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SetAgentActive_LogsAction()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var userId = createResult.Data!.User.Id;

        await _service.SetAgentActive(userId, false, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "PauseAgent");
        Assert.NotNull(log);
    }

    // --- GetAgentUsers ---

    [Fact]
    public async Task GetAgentUsers_ReturnsOnlyAgents()
    {
        await CreateHumanUser("human@test.com", "Human");
        var admin = await CreateAdminUser();
        await _service.CreateAgentUser("Agent One", null, admin.Id);
        await _service.CreateAgentUser("Agent Two", null, admin.Id);

        var result = await _service.GetAgentUsers();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, a => Assert.StartsWith("Agent", a.DisplayName));
    }

    [Fact]
    public async Task GetAgentUsers_EmptyWhenNoAgents()
    {
        await CreateHumanUser("human@test.com", "Human");

        var result = await _service.GetAgentUsers();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetAgentUsers_IncludesAssignedTaskCount()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var agentUser = createResult.Data!.User;

        var project = new Project { Name = "Test Project" };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var task1 = new TaskItem { Title = "Task 1", ProjectId = project.Id, CreatedByUserId = agentUser.Id, Status = TaskItemStatus.ToDo };
        var task2 = new TaskItem { Title = "Task 2", ProjectId = project.Id, CreatedByUserId = agentUser.Id, Status = TaskItemStatus.Done };
        _context.TaskItems.AddRange(task1, task2);
        await _context.SaveChangesAsync();

        _context.TaskAssignments.Add(new TaskAssignment { TaskItemId = task1.Id, UserId = agentUser.Id });
        _context.TaskAssignments.Add(new TaskAssignment { TaskItemId = task2.Id, UserId = agentUser.Id });
        await _context.SaveChangesAsync();

        var result = await _service.GetAgentUsers();

        Assert.True(result.Succeeded);
        var agent = result.Data!.Single();
        Assert.Equal(1, agent.AssignedTaskCount);
    }

    // --- DeleteAgentUser ---

    [Fact]
    public async Task DeleteAgentUser_Success()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var agentUserId = createResult.Data!.User.Id;

        var result = await _service.DeleteAgentUser(agentUserId, admin.Id);

        Assert.True(result.Succeeded);

        var user = await _userManager.FindByIdAsync(agentUserId);
        Assert.Null(user);
    }

    [Fact]
    public async Task DeleteAgentUser_RevokesAndRemovesPATs()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var agentUserId = createResult.Data!.User.Id;

        await _service.DeleteAgentUser(agentUserId, admin.Id);

        var tokens = await _context.PersonalAccessTokens
            .Where(t => t.UserId == agentUserId)
            .ToListAsync();
        Assert.Empty(tokens);
    }

    [Fact]
    public async Task DeleteAgentUser_LogsAction()
    {
        var admin = await CreateAdminUser();
        var createResult = await _service.CreateAgentUser("CodeBot", null, admin.Id);
        var agentUserId = createResult.Data!.User.Id;

        await _service.DeleteAgentUser(agentUserId, admin.Id);

        var log = await _context.AdminActivityLogs
            .FirstOrDefaultAsync(l => l.Action == "DeleteAgentUser");
        Assert.NotNull(log);
        Assert.Equal(admin.Id, log!.AdminUserId);
    }

    [Fact]
    public async Task DeleteAgentUser_NonAgent_ReturnsFailure()
    {
        var admin = await CreateAdminUser();
        var humanUser = await CreateHumanUser("human@test.com", "Human");

        var result = await _service.DeleteAgentUser(humanUser.Id, admin.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("not an agent", result.Errors[0]);
    }

    [Fact]
    public async Task DeleteAgentUser_UserNotFound_ReturnsFailure()
    {
        var admin = await CreateAdminUser();

        var result = await _service.DeleteAgentUser("nonexistent-id", admin.Id);

        Assert.False(result.Succeeded);
    }

    // --- Migration verification ---

    [Fact]
    public async Task ExistingUsers_HaveDefaultAgentValues()
    {
        var user = await CreateHumanUser("existing@test.com", "Existing User");

        var loaded = await _context.Users.FindAsync(user.Id);

        Assert.NotNull(loaded);
        Assert.False(loaded!.IsAgent);
        Assert.Null(loaded.AgentDescription);
        Assert.True(loaded.IsAgentActive);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
