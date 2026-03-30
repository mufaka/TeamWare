using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TeamWare.Web.Data;
using TeamWare.Web.Models;
using TeamWare.Web.Services;
using TeamWare.Web.ViewModels;

namespace TeamWare.Tests.Services;

/// <summary>
/// Phase 48: Security hardening and edge case tests for agent configuration.
/// Covers SACFG-NF-06, SACFG-TEST-10, and edge cases from Phase 48.2.
/// </summary>
public class AgentConfigurationSecurityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly AgentConfigurationService _service;
    private readonly AgentSecretEncryptor _encryptor;

    public AgentConfigurationSecurityTests()
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

    // --- 48.1 Security Hardening Tests ---

    [Fact]
    public async Task GetConfigurationAsync_MaskedSecrets_NeverContainPlaintext()
    {
        var user = await CreateAgentUser("sec-agent-1");
        var token = "ghp_SuperSecretToken12345";

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = token
        });

        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "SecureProject",
            Url = "https://github.com/org/repo.git",
            AccessToken = "ghp_RepoSecret67890xyz"
        });

        await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "secure-mcp",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = "Bearer secret-auth-header-value",
            Env = JsonSerializer.Serialize(new { API_KEY = "sk-secret-api-key-value" })
        });

        var result = await _service.GetConfigurationAsync(user.Id);

        Assert.True(result.Succeeded);
        var config = result.Data!;

        // Masked repo access token should never contain the full plaintext
        Assert.NotEqual(token, config.RepositoryAccessToken);
        Assert.Contains("****", config.RepositoryAccessToken!);

        // Masked repository access token
        var repo = Assert.Single(config.Repositories);
        Assert.NotEqual("ghp_RepoSecret67890xyz", repo.AccessToken);
        Assert.Contains("****", repo.AccessToken!);

        // Masked MCP server auth header
        var mcp = Assert.Single(config.McpServers);
        Assert.NotEqual("Bearer secret-auth-header-value", mcp.AuthHeader);
        Assert.Contains("****", mcp.AuthHeader!);

        // Masked MCP server env vars
        Assert.NotNull(mcp.Env);
        Assert.Contains("****", mcp.Env!);
    }

    [Fact]
    public async Task GetDecryptedConfigurationAsync_ReturnsFullPlaintextSecrets()
    {
        var user = await CreateAgentUser("sec-agent-2");
        var token = "ghp_DecryptedToken12345";

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = token
        });

        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Decrypted",
            Url = "https://github.com/org/repo.git",
            AccessToken = "ghp_FullRepoToken"
        });

        await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "decrypt-mcp",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = "Bearer full-auth-header"
        });

        var result = await _service.GetDecryptedConfigurationAsync(user.Id);

        Assert.True(result.Succeeded);
        var config = result.Data!;

        // Decrypted should contain the full plaintext
        Assert.Equal(token, config.RepositoryAccessToken);
        Assert.Equal("ghp_FullRepoToken", config.Repositories[0].AccessToken);
        Assert.Equal("Bearer full-auth-header", config.McpServers[0].AuthHeader);
    }

    [Fact]
    public async Task EncryptedFields_NotStoredAsPlaintext_InDatabase()
    {
        var user = await CreateAgentUser("sec-agent-3");
        var plainToken = "ghp_PlaintextToken999";

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = plainToken
        });

        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "Raw",
            Url = "https://github.com/org/repo.git",
            AccessToken = "ghp_RawToken"
        });

        await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "raw-mcp",
            Type = "http",
            Url = "https://mcp.example.com",
            AuthHeader = "Bearer raw-auth"
        });

        // Read raw entities from the database
        var rawConfig = await _context.AgentConfigurations
            .Include(ac => ac.Repositories)
            .Include(ac => ac.McpServers)
            .FirstAsync(ac => ac.UserId == user.Id);

        // Encrypted fields should NOT contain the plaintext
        Assert.NotNull(rawConfig.EncryptedRepositoryAccessToken);
        Assert.NotEqual(plainToken, rawConfig.EncryptedRepositoryAccessToken);

        Assert.NotNull(rawConfig.Repositories.First().EncryptedAccessToken);
        Assert.NotEqual("ghp_RawToken", rawConfig.Repositories.First().EncryptedAccessToken);

        Assert.NotNull(rawConfig.McpServers.First().EncryptedAuthHeader);
        Assert.NotEqual("Bearer raw-auth", rawConfig.McpServers.First().EncryptedAuthHeader);
    }

    [Fact]
    public async Task NonAgentUser_CannotAccessConfiguration()
    {
        var humanUser = await CreateUser("human-user", isAgent: false);

        var getResult = await _service.GetConfigurationAsync(humanUser.Id);
        Assert.False(getResult.Succeeded);

        var saveResult = await _service.SaveConfigurationAsync(humanUser.Id, new SaveAgentConfigurationDto());
        Assert.False(saveResult.Succeeded);

        var addRepoResult = await _service.AddRepositoryAsync(humanUser.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "P", Url = "https://x.git"
        });
        Assert.False(addRepoResult.Succeeded);

        var addMcpResult = await _service.AddMcpServerAsync(humanUser.Id, new SaveAgentMcpServerDto
        {
            Name = "m", Type = "http"
        });
        Assert.False(addMcpResult.Succeeded);
    }

    [Fact]
    public void MaskForDisplay_EdgeCase_TwoCharToken()
    {
        Assert.Equal("****", _encryptor.MaskForDisplay("ab"));
    }

    [Fact]
    public void MaskForDisplay_EdgeCase_ExactlyNineChars()
    {
        var result = _encryptor.MaskForDisplay("abcdefghi");
        Assert.Equal("abcd****ghi", result);
    }

    [Fact]
    public void MaskForDisplay_EdgeCase_WhitespaceToken()
    {
        // Whitespace is non-empty, so should be masked
        var result = _encryptor.MaskForDisplay("   ");
        Assert.Equal("****", result);
    }

    // --- 48.2 Edge Cases and Regression Tests ---

    [Fact]
    public async Task LargeSystemPrompt_10000Characters_HandledCorrectly()
    {
        var user = await CreateAgentUser("edge-agent-1");
        var largePrompt = new string('A', 10_000);

        var result = await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            SystemPrompt = largePrompt
        });

        Assert.True(result.Succeeded);

        var config = await _service.GetConfigurationAsync(user.Id);
        Assert.True(config.Succeeded);
        Assert.Equal(10_000, config.Data!.SystemPrompt!.Length);
        Assert.Equal(largePrompt, config.Data.SystemPrompt);
    }

    [Fact]
    public async Task UnicodeProjectNames_HandledCorrectly()
    {
        var user = await CreateAgentUser("edge-agent-2");

        var result = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "プロジェクト🎉",
            Url = "https://github.com/org/ünïcödë.git",
            Branch = "main"
        });

        Assert.True(result.Succeeded);

        var config = await _service.GetConfigurationAsync(user.Id);
        Assert.True(config.Succeeded);
        var repo = Assert.Single(config.Data!.Repositories);
        Assert.Equal("プロジェクト🎉", repo.ProjectName);
        Assert.Equal("https://github.com/org/ünïcödë.git", repo.Url);
    }

    [Fact]
    public async Task UnicodeInMcpServerFields_HandledCorrectly()
    {
        var user = await CreateAgentUser("edge-agent-3");

        var result = await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "MCP-サーバー",
            Type = "stdio",
            Command = "/usr/bin/ünïcödë-tool",
            Args = "[\"--ünïcödë\"]"
        });

        Assert.True(result.Succeeded);

        var config = await _service.GetConfigurationAsync(user.Id);
        Assert.True(config.Succeeded);
        var server = Assert.Single(config.Data!.McpServers);
        Assert.Equal("MCP-サーバー", server.Name);
        Assert.Equal("/usr/bin/ünïcödë-tool", server.Command);
    }

    [Fact]
    public async Task ConfigurationDeleted_NextGetReturnsNull()
    {
        var user = await CreateAgentUser("edge-agent-4");

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            Model = "test-model"
        });

        // Verify it exists
        var before = await _service.GetConfigurationAsync(user.Id);
        Assert.True(before.Succeeded);
        Assert.NotNull(before.Data);

        // Simulate deletion (admin deletes config or agent user is recreated)
        var config = await _context.AgentConfigurations.FirstAsync(ac => ac.UserId == user.Id);
        _context.AgentConfigurations.Remove(config);
        await _context.SaveChangesAsync();

        // Should return null (not error)
        var after = await _service.GetConfigurationAsync(user.Id);
        Assert.True(after.Succeeded);
        Assert.Null(after.Data);
    }

    [Fact]
    public async Task EncryptDecrypt_SpecialCharacters_InAccessTokens()
    {
        var user = await CreateAgentUser("edge-agent-5");
        var specialToken = "ghp_!@#$%^&*()_+-=[]{}|;':\",./<>?`~";

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = specialToken
        });

        var result = await _service.GetDecryptedConfigurationAsync(user.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(specialToken, result.Data!.RepositoryAccessToken);
    }

    [Fact]
    public async Task MultipleRepositories_UniqueProjectNameConstraint_Enforced()
    {
        var user = await CreateAgentUser("edge-agent-6");

        var first = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "DuplicateTest",
            Url = "https://github.com/org/first.git"
        });
        Assert.True(first.Succeeded);

        var second = await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "DuplicateTest",
            Url = "https://github.com/org/second.git"
        });
        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task SaveConfiguration_NullSecrets_DoesNotStoreGarbage()
    {
        var user = await CreateAgentUser("edge-agent-7");

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = null
        });

        var raw = await _context.AgentConfigurations.FirstAsync(ac => ac.UserId == user.Id);
        Assert.Null(raw.EncryptedRepositoryAccessToken);

        var config = await _service.GetConfigurationAsync(user.Id);
        Assert.True(config.Succeeded);
        Assert.Null(config.Data!.RepositoryAccessToken);
    }

    [Fact]
    public async Task SaveConfiguration_EmptyStringSecrets_TreatedAsNull()
    {
        var user = await CreateAgentUser("edge-agent-8");

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto
        {
            RepositoryAccessToken = string.Empty
        });

        var raw = await _context.AgentConfigurations.FirstAsync(ac => ac.UserId == user.Id);
        Assert.Null(raw.EncryptedRepositoryAccessToken);
    }

    [Fact]
    public async Task CascadeDelete_RemovesRepositoriesAndMcpServers()
    {
        var user = await CreateAgentUser("edge-agent-9");

        await _service.SaveConfigurationAsync(user.Id, new SaveAgentConfigurationDto { Model = "test" });
        await _service.AddRepositoryAsync(user.Id, new SaveAgentRepositoryDto
        {
            ProjectName = "CascadeRepo", Url = "https://x.git"
        });
        await _service.AddMcpServerAsync(user.Id, new SaveAgentMcpServerDto
        {
            Name = "cascade-mcp", Type = "http", Url = "https://mcp.example.com"
        });

        // Verify they exist
        Assert.True(await _context.AgentRepositories.AnyAsync());
        Assert.True(await _context.AgentMcpServers.AnyAsync());

        // Delete the user (triggers cascade)
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        Assert.False(await _context.AgentConfigurations.AnyAsync(ac => ac.UserId == user.Id));
        Assert.False(await _context.AgentRepositories.AnyAsync());
        Assert.False(await _context.AgentMcpServers.AnyAsync());
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
