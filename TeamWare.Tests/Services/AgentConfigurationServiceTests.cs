using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Tests.Services;

public class AgentConfigurationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly AgentConfigurationService _service;
    private readonly AgentSecretEncryptor _encryptor;

    public AgentConfigurationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        var provider = DataProtectionProvider.Create("TeamWare.Tests");
        _encryptor = new AgentSecretEncryptor(provider);
        _service = new AgentConfigurationService(_context, _encryptor);
    }

    [Fact]
    public async Task GetConfigurationAsync_NonExistentUser_ReturnsFailure()
    {
        var result = await _service.GetConfigurationAsync("nonexistent");

        Assert.False(result.Succeeded);
        Assert.Contains("User not found.", result.Errors);
    }

    [Fact]
    public async Task GetConfigurationAsync_NonAgentUser_ReturnsFailure()
    {
        var user = await CreateUser("human", isAgent: false);

        var result = await _service.GetConfigurationAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("User is not an agent.", result.Errors);
    }

    [Fact]
    public async Task GetConfigurationAsync_AgentWithNoConfig_ReturnsNull()
    {
        var user = await CreateAgentUser("agent1");

        var result = await _service.GetConfigurationAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task SaveConfigurationAsync_CreatesNewConfig()
    {
        var user = await CreateAgentUser("agent2");
        var dto = new SaveAgentConfigurationDto
        {
            PollingIntervalSeconds = 30,
            Model = "gpt-4o",
            AutoApproveTools = false,
            DryRun = true,
            TaskTimeoutSeconds = 300,
            SystemPrompt = "You are helpful."
        };

        var result = await _service.SaveConfigurationAsync(user.Id, dto);

        Assert.True(result.Succeeded);

        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.True(getResult.Succeeded);
        Assert.NotNull(getResult.Data);
        Assert.Equal(30, getResult.Data.PollingIntervalSeconds);
        Assert.Equal("gpt-4o", getResult.Data.Model);
        Assert.False(getResult.Data.AutoApproveTools);
        Assert.True(getResult.Data.DryRun);
        Assert.Equal(300, getResult.Data.TaskTimeoutSeconds);
        Assert.Equal("You are helpful.", getResult.Data.SystemPrompt);
    }

    [Fact]
    public async Task SaveConfigurationAsync_UpdatesExistingConfig()
    {
        var user = await CreateAgentUser("agent3");
        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto { Model = "gpt-4o" });

        var updateResult = await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto { Model = "gpt-4o-mini" });

        Assert.True(updateResult.Succeeded);
        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.Equal("gpt-4o-mini", getResult.Data!.Model);
    }

    [Fact]
    public async Task SaveConfigurationAsync_EncryptsRepositoryAccessToken()
    {
        var user = await CreateAgentUser("agent4");
        var dto = new SaveAgentConfigurationDto
        {
            RepositoryUrl = "https://github.com/org/repo",
            RepositoryAccessToken = "ghp_secret123token"
        };

        await _service.SaveConfigurationAsync(user.Id, dto);

        // Check raw entity — token should be encrypted
        var config = await _context.AgentConfigurations.FirstAsync(ac => ac.UserId == user.Id);
        Assert.NotNull(config.EncryptedRepositoryAccessToken);
        Assert.NotEqual("ghp_secret123token", config.EncryptedRepositoryAccessToken);

        // Masked view should show masked version
        var masked = await _service.GetConfigurationAsync(user.Id);
        Assert.Equal("ghp_****ken", masked.Data!.RepositoryAccessToken);

        // Decrypted view should show original
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("ghp_secret123token", decrypted.Data!.RepositoryAccessToken);
    }

    [Fact]
    public async Task SaveConfigurationAsync_NonAgentUser_ReturnsFailure()
    {
        var user = await CreateUser("human2", isAgent: false);

        var result = await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AddRepositoryAsync_CreatesRepository()
    {
        var user = await CreateAgentUser("agent5");
        var dto = new SaveAgentRepositoryDto
        {
            ProjectName = "TeamWare",
            Url = "https://github.com/org/teamware",
            Branch = "develop",
            AccessToken = "ghp_repotoken"
        };

        var result = await _service.AddRepositoryAsync(user.Id, dto);

        Assert.True(result.Succeeded);
        Assert.True(result.Data > 0);

        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.Single(getResult.Data!.Repositories);
        Assert.Equal("TeamWare", getResult.Data.Repositories[0].ProjectName);
        Assert.Equal("develop", getResult.Data.Repositories[0].Branch);
    }

    [Fact]
    public async Task AddRepositoryAsync_DuplicateProjectName_ReturnsFailure()
    {
        var user = await CreateAgentUser("agent6");
        var dto = new SaveAgentRepositoryDto
        {
            ProjectName = "DuplicateProject",
            Url = "https://github.com/org/repo"
        };

        await _service.AddRepositoryAsync(user.Id, dto);
        var result = await _service.AddRepositoryAsync(user.Id, dto);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Errors.First());
    }

    [Fact]
    public async Task AddRepositoryAsync_EncryptsAccessToken()
    {
        var user = await CreateAgentUser("agent7");
        var dto = new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo",
            AccessToken = "ghp_mysecrettoken123"
        };

        await _service.AddRepositoryAsync(user.Id, dto);

        // Raw entity should have encrypted token
        var repo = await _context.AgentRepositories.FirstAsync();
        Assert.NotEqual("ghp_mysecrettoken123", repo.EncryptedAccessToken);

        // Masked view
        var masked = await _service.GetConfigurationAsync(user.Id);
        Assert.Equal("ghp_****123", masked.Data!.Repositories[0].AccessToken);

        // Decrypted view
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("ghp_mysecrettoken123", decrypted.Data!.Repositories[0].AccessToken);
    }

    [Fact]
    public async Task UpdateRepositoryAsync_UpdatesFields()
    {
        var user = await CreateAgentUser("agent8");
        var addResult = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "OldName",
            Url = "https://github.com/org/old"
        });

        var updateResult = await _service.UpdateRepositoryAsync(addResult.Data, new SaveAgentRepositoryDto
        {
            ProjectName = "NewName",
            Url = "https://github.com/org/new",
            Branch = "feature"
        });

        Assert.True(updateResult.Succeeded);
        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.Equal("NewName", getResult.Data!.Repositories[0].ProjectName);
        Assert.Equal("https://github.com/org/new", getResult.Data.Repositories[0].Url);
        Assert.Equal("feature", getResult.Data.Repositories[0].Branch);
    }

    [Fact]
    public async Task UpdateRepositoryAsync_DuplicateProjectName_ReturnsFailure()
    {
        var user = await CreateAgentUser("agent9");
        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo1"
        });
        var second = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project2",
            Url = "https://github.com/org/repo2"
        });

        var result = await _service.UpdateRepositoryAsync(second.Data, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo2"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Errors.First());
    }

    [Fact]
    public async Task UpdateRepositoryAsync_NonExistentId_ReturnsFailure()
    {
        var result = await _service.UpdateRepositoryAsync(99999, new SaveAgentRepositoryDto());

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First());
    }

    [Fact]
    public async Task RemoveRepositoryAsync_RemovesRepository()
    {
        var user = await CreateAgentUser("agent10");
        var addResult = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "ToDelete",
            Url = "https://github.com/org/repo"
        });

        var result = await _service.RemoveRepositoryAsync(addResult.Data);

        Assert.True(result.Succeeded);
        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.NotNull(getResult.Data);
        Assert.Empty(getResult.Data.Repositories);
    }

    [Fact]
    public async Task RemoveRepositoryAsync_NonExistentId_ReturnsFailure()
    {
        var result = await _service.RemoveRepositoryAsync(99999);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AddMcpServerAsync_CreatesHttpServer()
    {
        var user = await CreateAgentUser("agent11");
        var dto = new SaveAgentMcpServerDto
        {
            Name = "teamware",
            Type = "http",
            Url = "https://teamware.example.com/mcp",
            AuthHeader = "Bearer secret-token"
        };

        var result = await _service.AddMcpServerAsync(user.Id, dto);

        Assert.True(result.Succeeded);
        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.Single(getResult.Data!.McpServers);
        Assert.Equal("teamware", getResult.Data.McpServers[0].Name);
        Assert.Equal("http", getResult.Data.McpServers[0].Type);
    }

    [Fact]
    public async Task AddMcpServerAsync_CreatesStdioServer()
    {
        var user = await CreateAgentUser("agent12");
        var dto = new SaveAgentMcpServerDto
        {
            Name = "filesystem",
            Type = "stdio",
            Command = "npx",
            Args = "[\"-y\", \"@modelcontextprotocol/server-filesystem\"]",
            Env = "{\"HOME\": \"/home/user\"}"
        };

        var result = await _service.AddMcpServerAsync(user.Id, dto);

        Assert.True(result.Succeeded);
        var getResult = await _service.GetDecryptedConfigurationAsync(user.Id);
        var server = getResult.Data!.McpServers[0];
        Assert.Equal("stdio", server.Type);
        Assert.Equal("npx", server.Command);
        Assert.Contains("server-filesystem", server.Args);
        Assert.Contains("HOME", server.Env);
    }

    [Fact]
    public async Task AddMcpServerAsync_EncryptsSecrets()
    {
        var user = await CreateAgentUser("agent13");
        var dto = new SaveAgentMcpServerDto
        {
            Name = "secure-server",
            Type = "http",
            Url = "https://example.com/mcp",
            AuthHeader = "Bearer my-long-secret-token-value"
        };

        await _service.AddMcpServerAsync(user.Id, dto);

        // Raw entity should have encrypted auth header
        var server = await _context.AgentMcpServers.FirstAsync();
        Assert.NotEqual("Bearer my-long-secret-token-value", server.EncryptedAuthHeader);

        // Masked view
        var masked = await _service.GetConfigurationAsync(user.Id);
        Assert.Equal("Bear****lue", masked.Data!.McpServers[0].AuthHeader);

        // Decrypted view
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("Bearer my-long-secret-token-value", decrypted.Data!.McpServers[0].AuthHeader);
    }

    [Fact]
    public async Task UpdateMcpServerAsync_UpdatesFields()
    {
        var user = await CreateAgentUser("agent14");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "old-server",
            Type = "http",
            Url = "https://old.example.com/mcp"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "new-server",
            Type = "stdio",
            Command = "node",
            Args = "[\"server.js\"]"
        });

        Assert.True(updateResult.Succeeded);
        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.Equal("new-server", getResult.Data!.McpServers[0].Name);
        Assert.Equal("stdio", getResult.Data.McpServers[0].Type);
        Assert.Equal("node", getResult.Data.McpServers[0].Command);
    }

    [Fact]
    public async Task UpdateMcpServerAsync_NonExistentId_ReturnsFailure()
    {
        var result = await _service.UpdateMcpServerAsync(99999, new SaveAgentMcpServerDto());

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Errors.First());
    }

    [Fact]
    public async Task RemoveMcpServerAsync_RemovesServer()
    {
        var user = await CreateAgentUser("agent15");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "to-delete",
            Type = "http",
            Url = "https://example.com/mcp"
        });

        var result = await _service.RemoveMcpServerAsync(addResult.Data);

        Assert.True(result.Succeeded);
        var getResult = await _service.GetConfigurationAsync(user.Id);
        Assert.NotNull(getResult.Data);
        Assert.Empty(getResult.Data.McpServers);
    }

    [Fact]
    public async Task RemoveMcpServerAsync_NonExistentId_ReturnsFailure()
    {
        var result = await _service.RemoveMcpServerAsync(99999);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetDecryptedConfigurationAsync_ReturnsFullSecrets()
    {
        var user = await CreateAgentUser("agent16");
        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryUrl = "https://github.com/org/repo",
            RepositoryAccessToken = "ghp_fullsecretvalue"
        });
        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/project1",
            AccessToken = "ghp_reposecret456"
        });
        await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://example.com/mcp",
            AuthHeader = "Bearer decrypted-secret"
        });

        var result = await _service.GetDecryptedConfigurationAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("ghp_fullsecretvalue", result.Data!.RepositoryAccessToken);
        Assert.Equal("ghp_reposecret456", result.Data.Repositories[0].AccessToken);
        Assert.Equal("Bearer decrypted-secret", result.Data.McpServers[0].AuthHeader);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReturnsMaskedSecrets()
    {
        var user = await CreateAgentUser("agent17");
        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = "ghp_fullsecretvalue"
        });
        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/project1",
            AccessToken = "ghp_reposecret456"
        });

        var result = await _service.GetConfigurationAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("ghp_****lue", result.Data!.RepositoryAccessToken);
        Assert.Equal("ghp_****456", result.Data.Repositories[0].AccessToken);
    }

    [Fact]
    public async Task SaveAndGet_NullSecrets_HandledGracefully()
    {
        var user = await CreateAgentUser("agent18");
        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            Model = "gpt-4o"
        });

        var result = await _service.GetConfigurationAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Null(result.Data!.RepositoryAccessToken);
    }

    [Fact]
    public async Task SaveConfigurationAsync_UpdatesTimestamp()
    {
        var user = await CreateAgentUser("agent19");
        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto { Model = "v1" });

        var config1 = await _context.AgentConfigurations.FirstAsync(ac => ac.UserId == user.Id);
        var firstUpdate = config1.UpdatedAt;

        await Task.Delay(10);
        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto { Model = "v2" });

        var config2 = await _context.AgentConfigurations.FirstAsync(ac => ac.UserId == user.Id);
        Assert.True(config2.UpdatedAt > firstUpdate);
    }

    // --- 51.1 Keep-Current Secret Logic: Repository ---

    [Fact]
    public async Task UpdateRepositoryAsync_BlankToken_KeepsExistingEncryptedValue()
    {
        var user = await CreateAgentUser("agent20");
        var addResult = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo",
            AccessToken = "ghp_originaltoken"
        });

        var updateResult = await _service.UpdateRepositoryAsync(addResult.Data, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo",
            Branch = "main",
            AccessToken = null,
            ClearAccessToken = false
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("ghp_originaltoken", decrypted.Data!.Repositories[0].AccessToken);
    }

    [Fact]
    public async Task UpdateRepositoryAsync_ClearToken_NullsStoredToken()
    {
        var user = await CreateAgentUser("agent21");
        var addResult = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo",
            AccessToken = "ghp_originaltoken"
        });

        var updateResult = await _service.UpdateRepositoryAsync(addResult.Data, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo",
            Branch = "main",
            AccessToken = "ghp_ignoredvalue",
            ClearAccessToken = true
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Null(decrypted.Data!.Repositories[0].AccessToken);
    }

    [Fact]
    public async Task UpdateRepositoryAsync_NewToken_EncryptsAndStoresIt()
    {
        var user = await CreateAgentUser("agent22");
        var addResult = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo"
        });

        var updateResult = await _service.UpdateRepositoryAsync(addResult.Data, new SaveAgentRepositoryDto
        {
            ProjectName = "Project1",
            Url = "https://github.com/org/repo",
            Branch = "main",
            AccessToken = "ghp_brandnewtoken",
            ClearAccessToken = false
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("ghp_brandnewtoken", decrypted.Data!.Repositories[0].AccessToken);
    }

    // --- 51.1 Keep-Current Secret Logic: MCP Server AuthHeader ---

    [Fact]
    public async Task UpdateMcpServerAsync_BlankAuthHeader_KeepsExistingEncryptedValue()
    {
        var user = await CreateAgentUser("agent23");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = "Bearer original-token"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = null,
            ClearAuthHeader = false
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("Bearer original-token", decrypted.Data!.McpServers[0].AuthHeader);
    }

    [Fact]
    public async Task UpdateMcpServerAsync_ClearAuthHeader_NullsStoredAuthHeader()
    {
        var user = await CreateAgentUser("agent24");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = "Bearer original-token"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://mcp.example.com",
            ClearAuthHeader = true
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Null(decrypted.Data!.McpServers[0].AuthHeader);
    }

    [Fact]
    public async Task UpdateMcpServerAsync_NewAuthHeader_EncryptsAndStoresIt()
    {
        var user = await CreateAgentUser("agent25");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://mcp.example.com"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = "Bearer new-token"
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("Bearer new-token", decrypted.Data!.McpServers[0].AuthHeader);
    }

    // --- 51.1 Keep-Current Secret Logic: MCP Server Env ---

    [Fact]
    public async Task UpdateMcpServerAsync_BlankEnv_KeepsExistingEncryptedValue()
    {
        var user = await CreateAgentUser("agent26");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "stdio",
            Command = "npx",
            Env = "{\"API_KEY\":\"original-key\"}"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "stdio",
            Command = "npx",
            Env = null,
            ClearEnv = false
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("{\"API_KEY\":\"original-key\"}", decrypted.Data!.McpServers[0].Env);
    }

    [Fact]
    public async Task UpdateMcpServerAsync_ClearEnv_NullsStoredEnv()
    {
        var user = await CreateAgentUser("agent27");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "stdio",
            Command = "npx",
            Env = "{\"API_KEY\":\"original-key\"}"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "stdio",
            Command = "npx",
            ClearEnv = true
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Null(decrypted.Data!.McpServers[0].Env);
    }

    [Fact]
    public async Task UpdateMcpServerAsync_NewEnv_EncryptsAndStoresIt()
    {
        var user = await CreateAgentUser("agent28");
        var addResult = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "stdio",
            Command = "npx"
        });

        var updateResult = await _service.UpdateMcpServerAsync(addResult.Data, new SaveAgentMcpServerDto
        {
            Name = "server1",
            Type = "stdio",
            Command = "npx",
            Env = "{\"API_KEY\":\"new-key\"}"
        });

        Assert.True(updateResult.Succeeded);
        var decrypted = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.Equal("{\"API_KEY\":\"new-key\"}", decrypted.Data!.McpServers[0].Env);
    }

    private async Task<ApplicationUser> CreateUser(string username, bool isAgent)
    {
        var user = new ApplicationUser
        {
            UserName = username,
            Email = $"{username}@example.com",
            DisplayName = $"User {username}",
            IsAgent = isAgent
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<ApplicationUser> CreateAgentUser(string username)
    {
        return await CreateUser(username, isAgent: true);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
