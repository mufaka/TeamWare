using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Mcp;

public class ProjectToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ProjectService _projectService;
    private readonly ProgressService _progressService;

    public ProjectToolsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _projectService = new ProjectService(_context);
        _progressService = new ProgressService(_context);
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

    // --- list_projects ---

    [Fact]
    public async Task ListProjects_ReturnsUserProjects()
    {
        var user = CreateUser("user@test.com", "Test User");
        await _projectService.CreateProject("Project A", "Description A", user.Id);
        await _projectService.CreateProject("Project B", null, user.Id);
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await ProjectTools.list_projects(principal, _projectService);

        using var doc = JsonDocument.Parse(result);
        var array = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(2, array.GetArrayLength());

        var first = array[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("name", out _));
        Assert.True(first.TryGetProperty("description", out _));
        Assert.True(first.TryGetProperty("status", out _));
        Assert.True(first.TryGetProperty("memberCount", out _));
        Assert.Equal("Active", first.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListProjects_NoProjects_ReturnsEmptyArray()
    {
        var user = CreateUser("lonely@test.com", "Lonely User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await ProjectTools.list_projects(principal, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListProjects_DoesNotIncludeNonMemberProjects()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var other = CreateUser("other@test.com", "Other");
        await _projectService.CreateProject("Owner's Project", null, owner.Id);
        var principal = CreateClaimsPrincipal(other.Id);

        var result = await ProjectTools.list_projects(principal, _projectService);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    // --- get_project ---

    [Fact]
    public async Task GetProject_ReturnsProjectDetailsAndStatistics()
    {
        var user = CreateUser("user@test.com", "Test User");
        var createResult = await _projectService.CreateProject("My Project", "A description", user.Id);
        var projectId = createResult.Data!.Id;
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await ProjectTools.get_project(principal, _projectService, _progressService, projectId);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("My Project", root.GetProperty("name").GetString());
        Assert.Equal("A description", root.GetProperty("description").GetString());
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("taskStatistics", out var stats));
        Assert.Equal(0, stats.GetProperty("totalTasks").GetInt32());
        Assert.True(root.TryGetProperty("overdueTasks", out _));
        Assert.True(root.TryGetProperty("upcomingDeadlines", out _));
        Assert.True(root.TryGetProperty("totalMembers", out _));
    }

    [Fact]
    public async Task GetProject_NonMember_ReturnsError()
    {
        var owner = CreateUser("owner@test.com", "Owner");
        var outsider = CreateUser("outsider@test.com", "Outsider");
        var createResult = await _projectService.CreateProject("Private Project", null, owner.Id);
        var projectId = createResult.Data!.Id;
        var principal = CreateClaimsPrincipal(outsider.Id);

        var result = await ProjectTools.get_project(principal, _projectService, _progressService, projectId);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("member", error.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProject_NonExistentProject_ReturnsError()
    {
        var user = CreateUser("user@test.com", "Test User");
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await ProjectTools.get_project(principal, _projectService, _progressService, 99999);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task GetProject_UsesIso8601Dates()
    {
        var user = CreateUser("dates@test.com", "Date User");
        var createResult = await _projectService.CreateProject("Date Project", null, user.Id);
        var principal = CreateClaimsPrincipal(user.Id);

        var result = await ProjectTools.get_project(principal, _projectService, _progressService, createResult.Data!.Id);

        using var doc = JsonDocument.Parse(result);
        var createdAt = doc.RootElement.GetProperty("createdAt").GetString();
        Assert.NotNull(createdAt);
        Assert.True(DateTime.TryParse(createdAt, out _), "createdAt should be a parseable date");
        Assert.Contains("T", createdAt); // ISO 8601 contains 'T'
    }
}
