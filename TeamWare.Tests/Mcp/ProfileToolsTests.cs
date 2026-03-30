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
using TeamWare.Web.ViewModels;

namespace TeamWare.Tests.Mcp;

public class ProfileToolsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAgentConfigurationService _agentConfigService;

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

        services.AddDataProtection()
            .UseEphemeralDataProtectionProvider();

        services.AddSingleton<IAgentSecretEncryptor, AgentSecretEncryptor>();
        services.AddScoped<IAgentConfigurationService, AgentConfigurationService>();

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        var context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated();

        _userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _agentConfigService = _serviceProvider.GetRequiredService<IAgentConfigurationService>();
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

        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
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

        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(humanUser.Id, root.GetProperty("userId").GetString());
        Assert.Equal("Human User", root.GetProperty("displayName").GetString());
        Assert.Equal("human@example.com", root.GetProperty("email").GetString());
        Assert.False(root.GetProperty("isAgent").GetBoolean());
        Assert.False(root.TryGetProperty("agentDescription", out _));
        Assert.False(root.TryGetProperty("isAgentActive", out _));
    }

    [Fact]
    public async Task GetMyProfile_UserNotFound_ReturnsError()
    {
        var principal = CreateClaimsPrincipal("nonexistent-user-id");

        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal("User not found.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMyProfile_AgentWithConfiguration_IncludesConfigurationBlock()
    {
        var agentUser = new ApplicationUser
        {
            UserName = "agent-config",
            Email = "agent-config@agent.local",
            DisplayName = "Config Agent",
            IsAgent = true,
            IsAgentActive = true
        };
        await _userManager.CreateAsync(agentUser, "TestPass1");

        await _agentConfigService.SaveConfigurationAsync(agentUser.Id, new SaveAgentConfigurationDto
        {
            PollingIntervalSeconds = 120,
            Model = "gpt-4o",
            AutoApproveTools = false,
            DryRun = true,
            TaskTimeoutSeconds = 900,
            SystemPrompt = "You are a test agent."
        });

        var principal = CreateClaimsPrincipal(agentUser.Id);
        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("configuration", out var configElement));
        Assert.Equal(JsonValueKind.Object, configElement.ValueKind);
        Assert.Equal(120, configElement.GetProperty("pollingIntervalSeconds").GetInt32());
        Assert.Equal("gpt-4o", configElement.GetProperty("model").GetString());
        Assert.False(configElement.GetProperty("autoApproveTools").GetBoolean());
        Assert.True(configElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(900, configElement.GetProperty("taskTimeoutSeconds").GetInt32());
        Assert.Equal("You are a test agent.", configElement.GetProperty("systemPrompt").GetString());
    }

    [Fact]
    public async Task GetMyProfile_AgentWithoutConfiguration_ConfigurationIsNull()
    {
        var agentUser = new ApplicationUser
        {
            UserName = "agent-noconfig",
            Email = "agent-noconfig@agent.local",
            DisplayName = "NoConfig Agent",
            IsAgent = true,
            IsAgentActive = true
        };
        await _userManager.CreateAsync(agentUser, "TestPass1");

        var principal = CreateClaimsPrincipal(agentUser.Id);
        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        // With WhenWritingNull, configuration should not be present when null
        Assert.False(root.TryGetProperty("configuration", out _));
    }

    [Fact]
    public async Task GetMyProfile_HumanUser_NoConfigurationField()
    {
        var humanUser = new ApplicationUser
        {
            UserName = "human-noconfig",
            Email = "human-noconfig@example.com",
            DisplayName = "Human NoConfig",
            IsAgent = false
        };
        await _userManager.CreateAsync(humanUser, "TestPass1");

        var principal = CreateClaimsPrincipal(humanUser.Id);
        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.False(root.TryGetProperty("configuration", out _));
    }

    [Fact]
    public async Task GetMyProfile_AgentWithRepositories_IncludesRepositoriesInConfig()
    {
        var agentUser = new ApplicationUser
        {
            UserName = "agent-repos",
            Email = "agent-repos@agent.local",
            DisplayName = "Repos Agent",
            IsAgent = true,
            IsAgentActive = true
        };
        await _userManager.CreateAsync(agentUser, "TestPass1");

        await _agentConfigService.AddRepositoryAsync(agentUser.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "TestProject",
            Url = "https://github.com/test/repo",
            Branch = "main",
            AccessToken = "ghp_test123"
        });

        var principal = CreateClaimsPrincipal(agentUser.Id);
        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        var config = root.GetProperty("configuration");
        var repos = config.GetProperty("repositories");
        Assert.Equal(JsonValueKind.Array, repos.ValueKind);
        Assert.Equal(1, repos.GetArrayLength());

        var repo = repos[0];
        Assert.Equal("TestProject", repo.GetProperty("projectName").GetString());
        Assert.Equal("https://github.com/test/repo", repo.GetProperty("url").GetString());
        Assert.Equal("main", repo.GetProperty("branch").GetString());
        // Decrypted secrets should appear for agent consumption
        Assert.Equal("ghp_test123", repo.GetProperty("accessToken").GetString());
    }

    [Fact]
    public async Task GetMyProfile_AgentWithMcpServers_IncludesMcpServersInConfig()
    {
        var agentUser = new ApplicationUser
        {
            UserName = "agent-mcp",
            Email = "agent-mcp@agent.local",
            DisplayName = "MCP Agent",
            IsAgent = true,
            IsAgentActive = true
        };
        await _userManager.CreateAsync(agentUser, "TestPass1");

        await _agentConfigService.AddMcpServerAsync(agentUser.Id, new SaveAgentMcpServerDto
        {
            Name = "github-mcp",
            Type = "http",
            Url = "https://mcp.github.com",
            AuthHeader = "Bearer ghp_secret"
        });

        var principal = CreateClaimsPrincipal(agentUser.Id);
        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        var config = root.GetProperty("configuration");
        var servers = config.GetProperty("mcpServers");
        Assert.Equal(JsonValueKind.Array, servers.ValueKind);
        Assert.Equal(1, servers.GetArrayLength());

        var server = servers[0];
        Assert.Equal("github-mcp", server.GetProperty("name").GetString());
        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.Equal("https://mcp.github.com", server.GetProperty("url").GetString());
        Assert.Equal("Bearer ghp_secret", server.GetProperty("authHeader").GetString());
    }

    [Fact]
    public async Task GetMyProfile_AgentWithStdioMcpServer_ArgsAndEnvSerializedAsJsonStructures()
    {
        var agentUser = new ApplicationUser
        {
            UserName = "agent-stdio",
            Email = "agent-stdio@agent.local",
            DisplayName = "Stdio Agent",
            IsAgent = true,
            IsAgentActive = true
        };
        await _userManager.CreateAsync(agentUser, "TestPass1");

        await _agentConfigService.AddMcpServerAsync(agentUser.Id, new SaveAgentMcpServerDto
        {
            Name = "local-mcp",
            Type = "stdio",
            Command = "npx",
            Args = "[\"--yes\", \"@modelcontextprotocol/server\"]",
            Env = "{\"NODE_ENV\":\"production\",\"PORT\":\"3000\"}"
        });

        var principal = CreateClaimsPrincipal(agentUser.Id);
        var result = await ProfileTools.get_my_profile(principal, _userManager, _agentConfigService);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        var config = root.GetProperty("configuration");
        var servers = config.GetProperty("mcpServers");
        Assert.Equal(1, servers.GetArrayLength());

        var server = servers[0];
        Assert.Equal("local-mcp", server.GetProperty("name").GetString());
        Assert.Equal("stdio", server.GetProperty("type").GetString());
        Assert.Equal("npx", server.GetProperty("command").GetString());

        // Args must be a JSON array, not a double-encoded string
        var args = server.GetProperty("args");
        Assert.Equal(JsonValueKind.Array, args.ValueKind);
        Assert.Equal(2, args.GetArrayLength());
        Assert.Equal("--yes", args[0].GetString());
        Assert.Equal("@modelcontextprotocol/server", args[1].GetString());

        // Env must be a JSON object, not a double-encoded string
        var env = server.GetProperty("env");
        Assert.Equal(JsonValueKind.Object, env.ValueKind);
        Assert.Equal("production", env.GetProperty("NODE_ENV").GetString());
        Assert.Equal("3000", env.GetProperty("PORT").GetString());
    }
}
