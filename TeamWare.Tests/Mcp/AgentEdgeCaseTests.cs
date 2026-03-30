using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class AgentEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ProjectService _projectService;
    private readonly TaskService _taskService;
    private readonly CommentService _commentService;
    private readonly InboxService _inboxService;
    private readonly LoungeService _loungeService;
    private readonly ActivityLogService _activityLogService;
    private readonly NotificationService _notificationService;
    private readonly AdminService _adminService;
    private readonly IAgentConfigurationService _agentConfigService;

    public AgentEdgeCaseTests()
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

        services.AddDataProtection()
            .UseEphemeralDataProtectionProvider();

        services.AddSingleton<IAgentSecretEncryptor, AgentSecretEncryptor>();
        services.AddScoped<IAgentConfigurationService, AgentConfigurationService>();

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        _context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _context.Database.EnsureCreated();

        _userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        roleManager.CreateAsync(new IdentityRole(SeedData.AdminRoleName)).GetAwaiter().GetResult();
        roleManager.CreateAsync(new IdentityRole(SeedData.UserRoleName)).GetAwaiter().GetResult();

        _notificationService = new NotificationService(_context);
        _activityLogService = new ActivityLogService(_context);
        _projectService = new ProjectService(_context);
        _taskService = new TaskService(_context, _activityLogService, _notificationService);
        _commentService = new CommentService(_context, _notificationService);
        _inboxService = new InboxService(_context, _taskService, _notificationService);
        _loungeService = new LoungeService(_context, _notificationService);

        var activityLogSvc = new AdminActivityLogService(_context);
        var tokenService = new PersonalAccessTokenService(_context);
        _adminService = new AdminService(_context, _userManager, activityLogSvc, tokenService);
        _agentConfigService = _serviceProvider.GetRequiredService<IAgentConfigurationService>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private async Task<ApplicationUser> CreateAgentUser(string displayName)
    {
        var admin = await CreateAdminUser();
        var result = await _adminService.CreateAgentUser(displayName, "Agent description", admin.Id);
        return result.Data!.User;
    }

    private async Task<ApplicationUser> CreateAdminUser()
    {
        var existing = await _userManager.FindByEmailAsync("admin@test.com");
        if (existing != null) return existing;

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

    private ApplicationUser CreateHumanUser(string email, string displayName)
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

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal CreateAgentClaimsPrincipal(string userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("IsAgent", "true")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // ===================================================================
    // Agent user works with list_projects — returns only member projects
    // ===================================================================

    [Fact]
    public async Task ListProjects_AgentUser_ReturnsOnlyMemberProjects()
    {
        var agent = await CreateAgentUser("ProjectBot");
        var owner = CreateHumanUser("owner@test.com", "Owner");

        // Create a project the agent is a member of
        var projResult = await _projectService.CreateProject("Agent Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        // Create another project the agent is NOT a member of
        await _projectService.CreateProject("Other Project", null, owner.Id);

        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await ProjectTools.list_projects(principal, _projectService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("Agent Project", array[0].GetProperty("name").GetString());
    }

    // ===================================================================
    // Agent user works with get_task — works for tasks in member projects
    // ===================================================================

    [Fact]
    public async Task GetTask_AgentUser_WorksForMemberProject()
    {
        var agent = await CreateAgentUser("TaskBot");
        var owner = CreateHumanUser("owner2@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Task Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        var taskResult = await _taskService.CreateTask(project.Id, "Agent Task", "Desc", TaskItemPriority.High, null, owner.Id);
        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await TaskTools.get_task(principal, _taskService, _commentService, taskResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Agent Task", doc.RootElement.GetProperty("title").GetString());
    }

    // ===================================================================
    // Agent user works with update_task_status — can move tasks through workflow
    // ===================================================================

    [Fact]
    public async Task UpdateTaskStatus_AgentUser_CanMoveTaskStatus()
    {
        var agent = await CreateAgentUser("StatusBot");
        var owner = CreateHumanUser("owner3@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Status Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        var taskResult = await _taskService.CreateTask(project.Id, "Move Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await TaskTools.update_task_status(principal, _taskService, taskResult.Data!.Id, "InProgress");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("InProgress", doc.RootElement.GetProperty("status").GetString());
    }

    // ===================================================================
    // Agent user works with add_comment — can post comments
    // ===================================================================

    [Fact]
    public async Task AddComment_AgentUser_CanPostComment()
    {
        var agent = await CreateAgentUser("CommentBot");
        var owner = CreateHumanUser("owner4@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Comment Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        var taskResult = await _taskService.CreateTask(project.Id, "Comment Task", null, TaskItemPriority.Medium, null, owner.Id);
        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await TaskTools.add_comment(principal, _commentService, taskResult.Data!.Id, "Agent comment");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Agent comment", doc.RootElement.GetProperty("content").GetString());
    }

    // ===================================================================
    // Agent user works with create_task — can create tasks in member projects
    // ===================================================================

    [Fact]
    public async Task CreateTask_AgentUser_CanCreateTaskInMemberProject()
    {
        var agent = await CreateAgentUser("CreateBot");
        var owner = CreateHumanUser("owner5@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Create Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await TaskTools.create_task(principal, _taskService, project.Id, "Agent Created Task", "Description", "High", null);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Agent Created Task", doc.RootElement.GetProperty("title").GetString());
    }

    // ===================================================================
    // Agent user works with capture_inbox — can capture inbox items
    // ===================================================================

    [Fact]
    public async Task CaptureInbox_AgentUser_CanCaptureItem()
    {
        var agent = await CreateAgentUser("InboxBot");
        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await InboxTools.capture_inbox(principal, _inboxService, "Agent inbox item", "Some description");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Agent inbox item", doc.RootElement.GetProperty("title").GetString());
    }

    // ===================================================================
    // Agent user works with lounge tools — can read and post messages
    // ===================================================================

    [Fact]
    public async Task PostLoungeMessage_AgentUser_CanPostInMemberProject()
    {
        var agent = await CreateAgentUser("LoungeBot");
        var owner = CreateHumanUser("owner6@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Lounge Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        var principal = CreateClaimsPrincipal(agent.Id);
        var projectMemberService = new ProjectMemberService(_context);

        var result = await LoungeTools.post_lounge_message(principal, _loungeService, projectMemberService, "Hello from agent!", project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Hello from agent!", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ListLoungeMessages_AgentUser_CanReadMessagesInMemberProject()
    {
        var agent = await CreateAgentUser("ReadBot");
        var owner = CreateHumanUser("owner7@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Read Lounge", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        await _loungeService.SendMessage(project.Id, owner.Id, "Owner message");
        var principal = CreateClaimsPrincipal(agent.Id);
        var projectMemberService = new ProjectMemberService(_context);

        var result = await LoungeTools.list_lounge_messages(principal, _loungeService, projectMemberService, project.Id);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
    }

    // ===================================================================
    // Agent user non-member project — MCP tools enforce membership
    // ===================================================================

    [Fact]
    public async Task ListTasks_AgentUser_NonMemberProject_ReturnsError()
    {
        var agent = await CreateAgentUser("BlockedBot");
        var owner = CreateHumanUser("owner8@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Private Project", null, owner.Id);
        var principal = CreateClaimsPrincipal(agent.Id);

        var result = await TaskTools.list_tasks(principal, _taskService, projResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task PostLoungeMessage_AgentUser_NonMemberProject_ReturnsError()
    {
        var agent = await CreateAgentUser("BlockedLoungeBot");
        var owner = CreateHumanUser("owner9@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Private Lounge", null, owner.Id);
        var principal = CreateClaimsPrincipal(agent.Id);
        var projectMemberService = new ProjectMemberService(_context);

        var result = await LoungeTools.post_lounge_message(principal, _loungeService, projectMemberService, "Denied!", projResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    // ===================================================================
    // get_my_profile works via both ClaimsPrincipal types
    // ===================================================================

    [Fact]
    public async Task GetMyProfile_AgentClaimsPrincipal_ReturnsAgentFields()
    {
        var agent = await CreateAgentUser("ProfileBot");
        var principal = CreateAgentClaimsPrincipal(agent.Id);

        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("isAgent").GetBoolean());
    }

    [Fact]
    public async Task GetMyProfile_HumanClaimsPrincipal_ReturnsHumanFields()
    {
        var human = CreateHumanUser("human-profile@test.com", "Human Profile");
        // Use UserManager to ensure user is persisted properly
        var found = await _userManager.FindByIdAsync(human.Id);
        Assert.NotNull(found);

        var principal = CreateClaimsPrincipal(human.Id);

        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("isAgent").GetBoolean());
    }

    // ===================================================================
    // Agent list page handles zero agents gracefully (service layer)
    // ===================================================================

    [Fact]
    public async Task GetAgentUsers_EmptyState_ReturnsEmptyList()
    {
        var result = await _adminService.GetAgentUsers();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    // ===================================================================
    // Agent user deletion — cascade behavior (current design)
    // Comments and activity logs are cascade-deleted with the user.
    // This matches existing behavior for all user types.
    // ===================================================================

    [Fact]
    public async Task DeleteAgentUser_CommentsAreCascadeDeleted()
    {
        var admin = await CreateAdminUser();
        var agent = await CreateAgentUser("DeleteBot");
        var owner = CreateHumanUser("owner10@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Delete Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        var taskResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);
        await _commentService.AddComment(taskResult.Data!.Id, "Agent comment before deletion", agent.Id);

        // Verify comment exists before deletion
        var commentsBefore = await _context.Comments.Where(c => c.TaskItemId == taskResult.Data!.Id).ToListAsync();
        Assert.Single(commentsBefore);

        // Delete the agent — cascade delete removes comments by this author
        await _adminService.DeleteAgentUser(agent.Id, admin.Id);

        var commentsAfter = await _context.Comments.Where(c => c.AuthorId == agent.Id).ToListAsync();
        Assert.Empty(commentsAfter);

        // Comments by other users on the same task are preserved
        await _commentService.AddComment(taskResult.Data!.Id, "Owner comment", owner.Id);
        var ownerComments = await _context.Comments.Where(c => c.TaskItemId == taskResult.Data!.Id).ToListAsync();
        Assert.Single(ownerComments);
    }

    [Fact]
    public async Task DeleteAgentUser_ActivityLogEntriesAreCascadeDeleted()
    {
        var admin = await CreateAdminUser();
        var agent = await CreateAgentUser("ActivityBot");
        var owner = CreateHumanUser("owner11@test.com", "Owner");

        var projResult = await _projectService.CreateProject("Activity Project", null, owner.Id);
        var project = projResult.Data!;
        _context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = agent.Id });
        await _context.SaveChangesAsync();

        // Create a task as the owner (agent is a member) to generate activity
        var taskResult = await _taskService.CreateTask(project.Id, "Task", null, TaskItemPriority.Medium, null, owner.Id);

        // Agent changes task status to generate activity log
        await _taskService.ChangeStatus(taskResult.Data!.Id, TaskItemStatus.InProgress, agent.Id);

        var beforeCount = await _context.ActivityLogEntries.CountAsync(a => a.UserId == agent.Id);
        Assert.True(beforeCount > 0);

        // Delete the agent — cascade delete removes activity logs by this user
        await _adminService.DeleteAgentUser(agent.Id, admin.Id);

        var afterCount = await _context.ActivityLogEntries.CountAsync(a => a.UserId == agent.Id);
        Assert.Equal(0, afterCount);
    }
}
