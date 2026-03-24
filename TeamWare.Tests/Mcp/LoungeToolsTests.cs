using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class LoungeToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly ProjectMemberService _projectMemberService;
    private readonly LoungeService _loungeService;
    private readonly NotificationService _notificationService;

    public LoungeToolsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notificationService = new NotificationService(_context);
        _loungeService = new LoungeService(_context, _notificationService);
        _projectService = new ProjectService(_context);
        _projectMemberService = new ProjectMemberService(_context);
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

    private async Task<(Project Project, ApplicationUser Owner)> CreateProjectWithOwner()
    {
        var owner = CreateUser($"owner-{Guid.NewGuid():N}@test.com", "Owner");
        var result = await _projectService.CreateProject("Test Project", null, owner.Id);
        return (result.Data!, owner);
    }

    // ===================================================================
    // list_lounge_messages
    // ===================================================================

    [Fact]
    public async Task ListLoungeMessages_GlobalLounge_ReturnsMessages()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _loungeService.SendMessage(null, user.Id, "Hello global!");
        await _loungeService.SendMessage(null, user.Id, "Second message");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.list_lounge_messages(
            principal, _loungeService, _projectMemberService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(2, array.GetArrayLength());

        var first = array[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("authorName", out _));
        Assert.True(first.TryGetProperty("content", out _));
        Assert.True(first.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task ListLoungeMessages_ProjectLounge_ReturnsProjectMessages()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _loungeService.SendMessage(project.Id, owner.Id, "Project message");
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await LoungeTools.list_lounge_messages(
            principal, _loungeService, _projectMemberService, projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal("Project message", array[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ListLoungeMessages_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var principal = CreateClaimsPrincipal(stranger.Id);

        var result = await LoungeTools.list_lounge_messages(
            principal, _loungeService, _projectMemberService, projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("not a member", error.GetString());
    }

    [Fact]
    public async Task ListLoungeMessages_DefaultCount_Returns20()
    {
        var user = CreateUser("user@test.com", "Test User");
        for (int i = 0; i < 25; i++)
        {
            await _loungeService.SendMessage(null, user.Id, $"Message {i}");
        }
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.list_lounge_messages(
            principal, _loungeService, _projectMemberService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(20, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListLoungeMessages_CustomCount_RespectsLimit()
    {
        var user = CreateUser("user@test.com", "Test User");
        for (int i = 0; i < 10; i++)
        {
            await _loungeService.SendMessage(null, user.Id, $"Message {i}");
        }
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.list_lounge_messages(
            principal, _loungeService, _projectMemberService, count: 5);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(5, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListLoungeMessages_CountClamped_Max100()
    {
        var user = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(user.Id);

        // Should not throw even with count > 100, it gets clamped
        var result = await LoungeTools.list_lounge_messages(
            principal, _loungeService, _projectMemberService, count: 200);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    // ===================================================================
    // post_lounge_message
    // ===================================================================

    [Fact]
    public async Task PostLoungeMessage_GlobalLounge_Success()
    {
        var user = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.post_lounge_message(
            principal, _loungeService, _projectMemberService, "Hello world!");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("id", out _));
        Assert.Equal("Hello world!", root.GetProperty("content").GetString());
        Assert.Equal("Test User", root.GetProperty("authorName").GetString());
        Assert.True(root.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task PostLoungeMessage_ProjectLounge_Success()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await LoungeTools.post_lounge_message(
            principal, _loungeService, _projectMemberService, "Project chat", projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Project chat", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task PostLoungeMessage_EmptyContent_ReturnsError()
    {
        var user = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.post_lounge_message(
            principal, _loungeService, _projectMemberService, "");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("required", error.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostLoungeMessage_ContentTooLong_ReturnsError()
    {
        var user = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(user.Id);
        var longContent = new string('x', 4001);

        var result = await LoungeTools.post_lounge_message(
            principal, _loungeService, _projectMemberService, longContent);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("4000", error.GetString());
    }

    [Fact]
    public async Task PostLoungeMessage_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var principal = CreateClaimsPrincipal(stranger.Id);

        var result = await LoungeTools.post_lounge_message(
            principal, _loungeService, _projectMemberService, "Unauthorized", projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("not a member", error.GetString());
    }

    [Fact]
    public async Task PostLoungeMessage_DelegatesToSendMessage_WhichHandlesMentions()
    {
        var user = CreateUser("user@test.com", "Test User");
        var mentioned = CreateUser("mentioneduser", "Mentioned User");
        var principal = CreateClaimsPrincipal(user.Id);

        // Post a message that includes a @mention via the MCP tool
        var result = await LoungeTools.post_lounge_message(
            principal, _loungeService, _projectMemberService, $"Hey @{mentioned.UserName} check this!");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("id", out _));
        // Verify the message content was preserved including the mention
        Assert.Contains($"@{mentioned.UserName}", root.GetProperty("content").GetString());

        // Verify the message was persisted via the service
        var messages = await _loungeService.GetMessages(null, null, 10);
        Assert.True(messages.Succeeded);
        Assert.Contains(messages.Data!, m => m.Content.Contains($"@{mentioned.UserName}"));
    }

    // ===================================================================
    // search_lounge_messages
    // ===================================================================

    [Fact]
    public async Task SearchLoungeMessages_FindsMatchingMessages()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _loungeService.SendMessage(null, user.Id, "Hello world!");
        await _loungeService.SendMessage(null, user.Id, "Goodbye world!");
        await _loungeService.SendMessage(null, user.Id, "No match here");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "world");

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(2, array.GetArrayLength());
    }

    [Fact]
    public async Task SearchLoungeMessages_CaseInsensitive()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _loungeService.SendMessage(null, user.Id, "Hello WORLD!");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "world");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task SearchLoungeMessages_NoMatches_ReturnsEmptyArray()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _loungeService.SendMessage(null, user.Id, "Hello there");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "nonexistent");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task SearchLoungeMessages_EmptyQuery_ReturnsError()
    {
        var user = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("required", error.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchLoungeMessages_ProjectLounge_FiltersCorrectly()
    {
        var (project, owner) = await CreateProjectWithOwner();
        await _loungeService.SendMessage(project.Id, owner.Id, "Project specific message");
        await _loungeService.SendMessage(null, owner.Id, "Global message with same word");
        var principal = CreateClaimsPrincipal(owner.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "message", projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Contains("Project specific", array[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SearchLoungeMessages_NonMember_ReturnsError()
    {
        var (project, owner) = await CreateProjectWithOwner();
        var stranger = CreateUser("stranger@test.com", "Stranger");
        var principal = CreateClaimsPrincipal(stranger.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "test", projectId: project.Id);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("not a member", error.GetString());
    }

    [Fact]
    public async Task SearchLoungeMessages_ReturnsCorrectJsonShape()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _loungeService.SendMessage(null, user.Id, "Searchable content");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await LoungeTools.search_lounge_messages(
            principal, _loungeService, _projectMemberService, "Searchable");

        using var doc = JsonDocument.Parse(result);
        var first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("authorName", out _));
        Assert.True(first.TryGetProperty("content", out _));
        Assert.True(first.TryGetProperty("createdAt", out _));
    }
}
