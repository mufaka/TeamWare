using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;

namespace TeamWare.Tests.Data;

public class AgentConfigurationEntityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public AgentConfigurationEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task MigrationAppliesCleanly_ExistingDataUnaffected()
    {
        // Arrange — create an existing user before adding agent config
        var user = new ApplicationUser
        {
            UserName = "existinguser",
            Email = "existing@example.com",
            DisplayName = "Existing User"
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act — verify the user still exists and has no agent config
        var retrieved = await _context.Users
            .Include(u => u.AgentConfiguration)
            .FirstOrDefaultAsync(u => u.UserName == "existinguser");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Existing User", retrieved.DisplayName);
        Assert.Null(retrieved.AgentConfiguration);
    }

    [Fact]
    public async Task CanCreateAgentConfiguration_WithAllFields()
    {
        var user = CreateAgentUser("agent1");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration
        {
            UserId = user.Id,
            PollingIntervalSeconds = 30,
            Model = "gpt-4o",
            AutoApproveTools = false,
            DryRun = true,
            TaskTimeoutSeconds = 300,
            SystemPrompt = "You are a helpful agent.",
            RepositoryUrl = "https://github.com/org/repo",
            RepositoryBranch = "develop",
            EncryptedRepositoryAccessToken = "encrypted-token-value"
        };

        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentConfigurations
            .Include(ac => ac.User)
            .FirstOrDefaultAsync(ac => ac.UserId == user.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(30, retrieved.PollingIntervalSeconds);
        Assert.Equal("gpt-4o", retrieved.Model);
        Assert.False(retrieved.AutoApproveTools);
        Assert.True(retrieved.DryRun);
        Assert.Equal(300, retrieved.TaskTimeoutSeconds);
        Assert.Equal("You are a helpful agent.", retrieved.SystemPrompt);
        Assert.Equal("https://github.com/org/repo", retrieved.RepositoryUrl);
        Assert.Equal("develop", retrieved.RepositoryBranch);
        Assert.Equal("encrypted-token-value", retrieved.EncryptedRepositoryAccessToken);
        Assert.Equal(user.Id, retrieved.User.Id);
    }

    [Fact]
    public async Task AgentConfiguration_NullableBehavioralFields_DefaultToNull()
    {
        var user = CreateAgentUser("agent2");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentConfigurations
            .FirstOrDefaultAsync(ac => ac.UserId == user.Id);

        Assert.NotNull(retrieved);
        Assert.Null(retrieved.PollingIntervalSeconds);
        Assert.Null(retrieved.Model);
        Assert.Null(retrieved.AutoApproveTools);
        Assert.Null(retrieved.DryRun);
        Assert.Null(retrieved.TaskTimeoutSeconds);
        Assert.Null(retrieved.SystemPrompt);
        Assert.Null(retrieved.RepositoryUrl);
        Assert.Null(retrieved.RepositoryBranch);
        Assert.Null(retrieved.EncryptedRepositoryAccessToken);
    }

    [Fact]
    public async Task AgentConfiguration_UserIdIsUnique()
    {
        var user = CreateAgentUser("agent3");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.AgentConfigurations.Add(new AgentConfiguration { UserId = user.Id });
        await _context.SaveChangesAsync();

        // Use raw SQL to bypass EF change tracker which would merge the entities
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO AgentConfigurations (UserId, CreatedAt, UpdatedAt) VALUES ({user.Id}, '2025-01-01', '2025-01-01')"));

        Assert.Contains("UNIQUE constraint failed", ex.Message);
    }

    [Fact]
    public async Task AgentConfiguration_CascadeDeletesWithUser()
    {
        var user = CreateAgentUser("agent4");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        Assert.Empty(await _context.AgentConfigurations.ToListAsync());
    }

    [Fact]
    public async Task CanCreateAgentRepository_WithConfiguration()
    {
        var user = CreateAgentUser("agent5");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var repo = new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "TeamWare",
            Url = "https://github.com/org/teamware",
            Branch = "main",
            EncryptedAccessToken = "encrypted-token",
            DisplayOrder = 0
        };
        _context.AgentRepositories.Add(repo);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentRepositories
            .Include(ar => ar.AgentConfiguration)
            .FirstOrDefaultAsync(ar => ar.ProjectName == "TeamWare");

        Assert.NotNull(retrieved);
        Assert.Equal("https://github.com/org/teamware", retrieved.Url);
        Assert.Equal("main", retrieved.Branch);
        Assert.Equal(config.Id, retrieved.AgentConfiguration.Id);
    }

    [Fact]
    public async Task AgentRepository_ProjectNameUniquePerConfiguration()
    {
        var user = CreateAgentUser("agent6");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _context.AgentRepositories.Add(new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "DuplicateProject",
            Url = "https://github.com/org/repo1"
        });
        await _context.SaveChangesAsync();

        _context.AgentRepositories.Add(new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "DuplicateProject",
            Url = "https://github.com/org/repo2"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task AgentRepository_CascadeDeletesWithConfiguration()
    {
        var user = CreateAgentUser("agent7");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _context.AgentRepositories.Add(new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "Project1",
            Url = "https://github.com/org/repo"
        });
        await _context.SaveChangesAsync();

        _context.AgentConfigurations.Remove(config);
        await _context.SaveChangesAsync();

        Assert.Empty(await _context.AgentRepositories.ToListAsync());
    }

    [Fact]
    public async Task AgentRepository_BranchDefaultsToMain()
    {
        var user = CreateAgentUser("agent8");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var repo = new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "TestProject",
            Url = "https://github.com/org/repo"
        };
        _context.AgentRepositories.Add(repo);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentRepositories.FirstAsync();
        Assert.Equal("main", retrieved.Branch);
    }

    [Fact]
    public async Task CanCreateAgentMcpServer_HttpType()
    {
        var user = CreateAgentUser("agent9");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var server = new AgentMcpServer
        {
            AgentConfigurationId = config.Id,
            Name = "teamware",
            Type = "http",
            Url = "https://teamware.example.com/mcp",
            EncryptedAuthHeader = "encrypted-header",
            DisplayOrder = 0
        };
        _context.AgentMcpServers.Add(server);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentMcpServers
            .Include(ms => ms.AgentConfiguration)
            .FirstOrDefaultAsync(ms => ms.Name == "teamware");

        Assert.NotNull(retrieved);
        Assert.Equal("http", retrieved.Type);
        Assert.Equal("https://teamware.example.com/mcp", retrieved.Url);
        Assert.Equal("encrypted-header", retrieved.EncryptedAuthHeader);
        Assert.Null(retrieved.Command);
        Assert.Null(retrieved.Args);
        Assert.Null(retrieved.EncryptedEnv);
    }

    [Fact]
    public async Task CanCreateAgentMcpServer_StdioType()
    {
        var user = CreateAgentUser("agent10");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var server = new AgentMcpServer
        {
            AgentConfigurationId = config.Id,
            Name = "filesystem",
            Type = "stdio",
            Command = "npx",
            Args = "[\"-y\", \"@modelcontextprotocol/server-filesystem\"]",
            EncryptedEnv = "encrypted-env-json",
            DisplayOrder = 1
        };
        _context.AgentMcpServers.Add(server);
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentMcpServers.FirstOrDefaultAsync(ms => ms.Name == "filesystem");

        Assert.NotNull(retrieved);
        Assert.Equal("stdio", retrieved.Type);
        Assert.Equal("npx", retrieved.Command);
        Assert.Contains("server-filesystem", retrieved.Args);
        Assert.Equal("encrypted-env-json", retrieved.EncryptedEnv);
        Assert.Null(retrieved.Url);
        Assert.Null(retrieved.EncryptedAuthHeader);
    }

    [Fact]
    public async Task AgentMcpServer_CascadeDeletesWithConfiguration()
    {
        var user = CreateAgentUser("agent11");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _context.AgentMcpServers.Add(new AgentMcpServer
        {
            AgentConfigurationId = config.Id,
            Name = "server1",
            Type = "http",
            Url = "https://example.com/mcp"
        });
        await _context.SaveChangesAsync();

        _context.AgentConfigurations.Remove(config);
        await _context.SaveChangesAsync();

        Assert.Empty(await _context.AgentMcpServers.ToListAsync());
    }

    [Fact]
    public async Task AgentConfiguration_NavigationLoadsRepositoriesAndMcpServers()
    {
        var user = CreateAgentUser("agent12");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration { UserId = user.Id };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _context.AgentRepositories.Add(new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "Repo1",
            Url = "https://github.com/org/repo1"
        });
        _context.AgentRepositories.Add(new AgentRepository
        {
            AgentConfigurationId = config.Id,
            ProjectName = "Repo2",
            Url = "https://github.com/org/repo2"
        });
        _context.AgentMcpServers.Add(new AgentMcpServer
        {
            AgentConfigurationId = config.Id,
            Name = "server1",
            Type = "http",
            Url = "https://example.com/mcp"
        });
        await _context.SaveChangesAsync();

        var retrieved = await _context.AgentConfigurations
            .Include(ac => ac.Repositories)
            .Include(ac => ac.McpServers)
            .FirstOrDefaultAsync(ac => ac.Id == config.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Repositories.Count);
        Assert.Single(retrieved.McpServers);
    }

    [Fact]
    public async Task ApplicationUser_NavigatesToAgentConfiguration()
    {
        var user = CreateAgentUser("agent13");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var config = new AgentConfiguration
        {
            UserId = user.Id,
            Model = "gpt-4o"
        };
        _context.AgentConfigurations.Add(config);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Users
            .Include(u => u.AgentConfiguration)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.AgentConfiguration);
        Assert.Equal("gpt-4o", retrieved.AgentConfiguration.Model);
    }

    private static ApplicationUser CreateAgentUser(string username)
    {
        return new ApplicationUser
        {
            UserName = username,
            Email = $"{username}@example.com",
            DisplayName = $"Agent {username}",
            IsAgent = true,
            IsAgentActive = true
        };
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
