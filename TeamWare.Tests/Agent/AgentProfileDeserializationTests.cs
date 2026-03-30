using System.Text.Json;

namespace TeamWare.Tests.Agent;

public class AgentProfileDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void AgentProfile_WithConfiguration_DeserializesCorrectly()
    {
        var json = """
        {
            "userId": "user-123",
            "displayName": "Test Agent",
            "email": "agent@test.com",
            "isAgent": true,
            "agentDescription": "A test agent",
            "isAgentActive": true,
            "lastActiveAt": "2025-01-15T10:30:00.0000000Z",
            "configuration": {
                "pollingIntervalSeconds": 120,
                "model": "gpt-4o",
                "autoApproveTools": false,
                "dryRun": true,
                "taskTimeoutSeconds": 900,
                "systemPrompt": "You are a test agent.",
                "repositoryUrl": "https://github.com/test/repo",
                "repositoryBranch": "main",
                "repositoryAccessToken": "ghp_test123"
            }
        }
        """;

        var profile = JsonSerializer.Deserialize<TeamWare.Agent.Mcp.AgentProfile>(json, JsonOptions);

        Assert.NotNull(profile);
        Assert.Equal("user-123", profile.UserId);
        Assert.Equal("Test Agent", profile.DisplayName);
        Assert.True(profile.IsAgent);
        Assert.NotNull(profile.Configuration);
        Assert.Equal(120, profile.Configuration.PollingIntervalSeconds);
        Assert.Equal("gpt-4o", profile.Configuration.Model);
        Assert.False(profile.Configuration.AutoApproveTools);
        Assert.True(profile.Configuration.DryRun);
        Assert.Equal(900, profile.Configuration.TaskTimeoutSeconds);
        Assert.Equal("You are a test agent.", profile.Configuration.SystemPrompt);
        Assert.Equal("https://github.com/test/repo", profile.Configuration.RepositoryUrl);
        Assert.Equal("main", profile.Configuration.RepositoryBranch);
        Assert.Equal("ghp_test123", profile.Configuration.RepositoryAccessToken);
    }

    [Fact]
    public void AgentProfile_WithRepositories_DeserializesCorrectly()
    {
        var json = """
        {
            "userId": "user-456",
            "displayName": "Repo Agent",
            "isAgent": true,
            "isAgentActive": true,
            "configuration": {
                "repositories": [
                    {
                        "projectName": "ProjectA",
                        "url": "https://github.com/org/projecta",
                        "branch": "main",
                        "accessToken": "ghp_tokenA"
                    },
                    {
                        "projectName": "ProjectB",
                        "url": "https://github.com/org/projectb",
                        "branch": "develop",
                        "accessToken": null
                    }
                ]
            }
        }
        """;

        var profile = JsonSerializer.Deserialize<TeamWare.Agent.Mcp.AgentProfile>(json, JsonOptions);

        Assert.NotNull(profile);
        Assert.NotNull(profile.Configuration);
        Assert.NotNull(profile.Configuration.Repositories);
        Assert.Equal(2, profile.Configuration.Repositories.Count);

        Assert.Equal("ProjectA", profile.Configuration.Repositories[0].ProjectName);
        Assert.Equal("https://github.com/org/projecta", profile.Configuration.Repositories[0].Url);
        Assert.Equal("main", profile.Configuration.Repositories[0].Branch);
        Assert.Equal("ghp_tokenA", profile.Configuration.Repositories[0].AccessToken);

        Assert.Equal("ProjectB", profile.Configuration.Repositories[1].ProjectName);
        Assert.Equal("develop", profile.Configuration.Repositories[1].Branch);
        Assert.Null(profile.Configuration.Repositories[1].AccessToken);
    }

    [Fact]
    public void AgentProfile_WithMcpServers_DeserializesCorrectly()
    {
        var json = """
        {
            "userId": "user-789",
            "displayName": "MCP Agent",
            "isAgent": true,
            "isAgentActive": true,
            "configuration": {
                "mcpServers": [
                    {
                        "name": "github-mcp",
                        "type": "http",
                        "url": "https://mcp.github.com",
                        "authHeader": "Bearer ghp_secret"
                    },
                    {
                        "name": "local-tool",
                        "type": "stdio",
                        "command": "npx",
                        "args": ["-y", "@modelcontextprotocol/server"],
                        "env": { "API_KEY": "secret123" }
                    }
                ]
            }
        }
        """;

        var profile = JsonSerializer.Deserialize<TeamWare.Agent.Mcp.AgentProfile>(json, JsonOptions);

        Assert.NotNull(profile);
        Assert.NotNull(profile.Configuration);
        Assert.NotNull(profile.Configuration.McpServers);
        Assert.Equal(2, profile.Configuration.McpServers.Count);

        var httpServer = profile.Configuration.McpServers[0];
        Assert.Equal("github-mcp", httpServer.Name);
        Assert.Equal("http", httpServer.Type);
        Assert.Equal("https://mcp.github.com", httpServer.Url);
        Assert.Equal("Bearer ghp_secret", httpServer.AuthHeader);
        Assert.Null(httpServer.Command);

        var stdioServer = profile.Configuration.McpServers[1];
        Assert.Equal("local-tool", stdioServer.Name);
        Assert.Equal("stdio", stdioServer.Type);
        Assert.Equal("npx", stdioServer.Command);
        Assert.NotNull(stdioServer.Args);
        Assert.Equal(2, stdioServer.Args.Count);
        Assert.Equal("-y", stdioServer.Args[0]);
        Assert.NotNull(stdioServer.Env);
        Assert.Equal("secret123", stdioServer.Env["API_KEY"]);
    }

    [Fact]
    public void AgentProfile_WithNullConfiguration_DeserializesCorrectly()
    {
        var json = """
        {
            "userId": "user-000",
            "displayName": "NoConfig Agent",
            "isAgent": true,
            "isAgentActive": true
        }
        """;

        var profile = JsonSerializer.Deserialize<TeamWare.Agent.Mcp.AgentProfile>(json, JsonOptions);

        Assert.NotNull(profile);
        Assert.Null(profile.Configuration);
    }

    [Fact]
    public void AgentProfile_WithPartialConfiguration_DeserializesCorrectly()
    {
        var json = """
        {
            "userId": "user-partial",
            "displayName": "Partial Config Agent",
            "isAgent": true,
            "isAgentActive": true,
            "configuration": {
                "pollingIntervalSeconds": 90,
                "model": "claude-sonnet"
            }
        }
        """;

        var profile = JsonSerializer.Deserialize<TeamWare.Agent.Mcp.AgentProfile>(json, JsonOptions);

        Assert.NotNull(profile);
        Assert.NotNull(profile.Configuration);
        Assert.Equal(90, profile.Configuration.PollingIntervalSeconds);
        Assert.Equal("claude-sonnet", profile.Configuration.Model);
        Assert.Null(profile.Configuration.AutoApproveTools);
        Assert.Null(profile.Configuration.DryRun);
        Assert.Null(profile.Configuration.TaskTimeoutSeconds);
        Assert.Null(profile.Configuration.SystemPrompt);
        Assert.Null(profile.Configuration.Repositories);
        Assert.Null(profile.Configuration.McpServers);
    }

    [Fact]
    public void AgentProfile_HumanUser_NoConfiguration_DeserializesCorrectly()
    {
        var json = """
        {
            "userId": "human-123",
            "displayName": "Human User",
            "email": "human@example.com",
            "isAgent": false
        }
        """;

        var profile = JsonSerializer.Deserialize<TeamWare.Agent.Mcp.AgentProfile>(json, JsonOptions);

        Assert.NotNull(profile);
        Assert.False(profile.IsAgent);
        Assert.Null(profile.Configuration);
    }
}
