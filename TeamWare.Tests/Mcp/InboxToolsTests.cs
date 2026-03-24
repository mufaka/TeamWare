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

    public InboxToolsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

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
}
