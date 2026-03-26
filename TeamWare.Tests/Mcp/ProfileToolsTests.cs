using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Web.Data;
using TeamWare.Web.Mcp.Tools;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Mcp;

public class ProfileToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProfileToolsTests()
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

        var context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated();

        _userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public async Task GetMyProfile_AgentUser_ReturnsAgentFields()
    {
        var agentUser = new ApplicationUser
        {
            UserName = "agent-test",
            Email = "agent-test@agent.local",
            DisplayName = "Test Agent",
            IsAgent = true,
            AgentDescription = "A helpful agent",
            IsAgentActive = true,
            LastActiveAt = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc)
        };
        await _userManager.CreateAsync(agentUser, "TestPass1");

        var principal = CreateClaimsPrincipal(agentUser.Id);

        var result = await ProfileTools.get_my_profile(principal, _userManager);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(agentUser.Id, root.GetProperty("userId").GetString());
        Assert.Equal("Test Agent", root.GetProperty("displayName").GetString());
        Assert.Equal("agent-test@agent.local", root.GetProperty("email").GetString());
        Assert.True(root.GetProperty("isAgent").GetBoolean());
        Assert.Equal("A helpful agent", root.GetProperty("agentDescription").GetString());
        Assert.True(root.GetProperty("isAgentActive").GetBoolean());
        Assert.Equal("2025-01-15T10:30:00.0000000Z", root.GetProperty("lastActiveAt").GetString());
    }

    [Fact]
    public async Task GetMyProfile_HumanUser_ReturnsNullForAgentFields()
    {
        var humanUser = new ApplicationUser
        {
            UserName = "human-user",
            Email = "human@example.com",
            DisplayName = "Human User",
            IsAgent = false,
            LastActiveAt = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc)
        };
        await _userManager.CreateAsync(humanUser, "TestPass1");

        var principal = CreateClaimsPrincipal(humanUser.Id);

        var result = await ProfileTools.get_my_profile(principal, _userManager);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(humanUser.Id, root.GetProperty("userId").GetString());
        Assert.Equal("Human User", root.GetProperty("displayName").GetString());
        Assert.Equal("human@example.com", root.GetProperty("email").GetString());
        Assert.False(root.GetProperty("isAgent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("agentDescription").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("isAgentActive").ValueKind);
    }

    [Fact]
    public async Task GetMyProfile_UserNotFound_ReturnsError()
    {
        var principal = CreateClaimsPrincipal("nonexistent-user-id");

        var result = await ProfileTools.get_my_profile(principal, _userManager);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal("User not found.", root.GetProperty("error").GetString());
    }
}
