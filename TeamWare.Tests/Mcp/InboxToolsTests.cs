using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class InboxToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly InboxService _inboxService;
    private readonly ProjectService _projectService;

    public InboxToolsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _projectService = new ProjectService(_context);
        var activityLogService = new ActivityLogService(_context);
        var notificationService = new NotificationService(_context);
        var taskService = new TaskService(_context, activityLogService, notificationService);
        _inboxService = new InboxService(_context, taskService, notificationService);
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

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // --- my_inbox ---

    [Fact]
    public async Task MyInbox_ReturnsUnprocessedItems()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _inboxService.AddItem("Inbox Item 1", "Description 1", user.Id);
        await _inboxService.AddItem("Inbox Item 2", null, user.Id);
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await InboxTools.my_inbox(principal, _inboxService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(2, array.GetArrayLength());

        var first = array[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("title", out _));
        Assert.True(first.TryGetProperty("description", out _));
        Assert.True(first.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task MyInbox_NoItems_ReturnsEmptyArray()
    {
        var user = CreateUser("empty@test.com", "Empty User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await InboxTools.my_inbox(principal, _inboxService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task MyInbox_DoesNotReturnOtherUsersItems()
    {
        var user1 = CreateUser("user1@test.com", "User 1");
        var user2 = CreateUser("user2@test.com", "User 2");
        await _inboxService.AddItem("User1 Item", null, user1.Id);
        var principal = CreateClaimsPrincipal(user2.Id);

        var result = await InboxTools.my_inbox(principal, _inboxService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task MyInbox_UsesIso8601Dates()
    {
        var user = CreateUser("dates@test.com", "Date User");
        await _inboxService.AddItem("Date Item", null, user.Id);
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await InboxTools.my_inbox(principal, _inboxService);

        using var doc = JsonDocument.Parse(result);
        var createdAt = doc.RootElement[0].GetProperty("createdAt").GetString();
        Assert.NotNull(createdAt);
        Assert.Contains("T", createdAt);
    }

    // --- capture_inbox ---

    [Fact]
    public async Task CaptureInbox_ReturnsCreatedItem()
    {
        var user = CreateUser("capture@test.com", "Capture User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await InboxTools.capture_inbox(principal, _inboxService, "New Inbox Item", "Some description");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("New Inbox Item", root.GetProperty("title").GetString());
        Assert.Equal("Some description", root.GetProperty("description").GetString());
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task CaptureInbox_WithoutDescription_Succeeds()
    {
        var user = CreateUser("capture-nodesc@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await InboxTools.capture_inbox(principal, _inboxService, "No Desc Item");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("No Desc Item", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CaptureInbox_EmptyTitle_ReturnsError()
    {
        var user = CreateUser("capture-empty@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await InboxTools.capture_inbox(principal, _inboxService, "");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Title is required", error.GetString());
    }

    [Fact]
    public async Task CaptureInbox_TitleTooLong_ReturnsError()
    {
        var user = CreateUser("capture-long@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);
        var longTitle = new string('A', 301);

        var result = await InboxTools.capture_inbox(principal, _inboxService, longTitle);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("300 characters", error.GetString());
    }

    [Fact]
    public async Task CaptureInbox_DescriptionTooLong_ReturnsError()
    {
        var user = CreateUser("capture-longdesc@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);
        var longDesc = new string('A', 4001);

        var result = await InboxTools.capture_inbox(principal, _inboxService, "Title", longDesc);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("4000 characters", error.GetString());
    }

    [Fact]
    public async Task CaptureInbox_ItemAppearsInMyInbox()
    {
        var user = CreateUser("capture-verify@test.com", "User");
        var principal = CreateClaimsPrincipal(user.Id);

        await InboxTools.capture_inbox(principal, _inboxService, "Verify Item", "Desc");

        var inboxResult = await InboxTools.my_inbox(principal, _inboxService);
        using var doc = JsonDocument.Parse(inboxResult);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("Verify Item", doc.RootElement[0].GetProperty("title").GetString());
    }

    // --- process_inbox_item ---

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner()
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject("Test Project", null, owner.Id);
        return (result.Data!, owner);
    }

    [Fact]
    public async Task ProcessInboxItem_ConvertsToTask()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Process Me", "Description", owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await InboxTools.process_inbox_item(principal, _inboxService, addResult.Data!.Id, project.Id, "High");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("Process Me", root.GetProperty("title").GetString());
        Assert.Equal("High", root.GetProperty("priority").GetString());
        Assert.Equal("ToDo", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("projectId", out _));
        Assert.True(root.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task ProcessInboxItem_WithNextAction_SetsFlag()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Next Action Item", null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await InboxTools.process_inbox_item(principal, _inboxService, addResult.Data!.Id, project.Id, "Medium", isNextAction: true);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("isNextAction").GetBoolean());
    }

    [Fact]
    public async Task ProcessInboxItem_WithSomedayMaybe_SetsFlag()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Someday Item", null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await InboxTools.process_inbox_item(principal, _inboxService, addResult.Data!.Id, project.Id, "Low", isSomedayMaybe: true);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("isSomedayMaybe").GetBoolean());
    }

    [Fact]
    public async Task ProcessInboxItem_InvalidPriority_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Item", null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await InboxTools.process_inbox_item(principal, _inboxService, addResult.Data!.Id, project.Id, "Extreme");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid priority", error.GetString());
    }

    [Fact]
    public async Task ProcessInboxItem_NonExistent_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await InboxTools.process_inbox_item(principal, _inboxService, 99999, project.Id, "Medium");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ProcessInboxItem_RemovesFromInbox()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var addResult = await _inboxService.AddItem("Remove Me", null, owner.Id);
        var principal = CreateClaimsPrincipal(owner.Id);

        await InboxTools.process_inbox_item(principal, _inboxService, addResult.Data!.Id, project.Id, "Medium");

        var inboxResult = await InboxTools.my_inbox(principal, _inboxService);
        using var doc = JsonDocument.Parse(inboxResult);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }
}
