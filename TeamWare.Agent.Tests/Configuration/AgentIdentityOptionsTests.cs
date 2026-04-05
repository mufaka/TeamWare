using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Tests.Configuration;

public class AgentIdentityOptionsTests
{
    [Fact]
    public void DefaultValues_AreApplied()
    {
        var options = new AgentIdentityOptions();

        Assert.Equal(string.Empty, options.Name);
        Assert.Equal(string.Empty, options.WorkingDirectory);
        Assert.Null(options.RepositoryUrl);
        Assert.Null(options.RepositoryBranch);
        Assert.Null(options.RepositoryAccessToken);
        Assert.Equal(string.Empty, options.PersonalAccessToken);
        Assert.Equal(60, options.PollingIntervalSeconds);
        Assert.Null(options.Model);
        Assert.True(options.AutoApproveTools);
        Assert.False(options.DryRun);
        Assert.Null(options.SystemPrompt);
        Assert.Empty(options.McpServers);
        Assert.Empty(options.Repositories);
    }

    [Fact]
    public void Configuration_LoadsCorrectlyFromJson()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("TestData/single-agent.json")
            .Build();

        var agents = new List<AgentIdentityOptions>();
        config.GetSection("Agents").Bind(agents);

        Assert.Single(agents);
        var agent = agents[0];
        Assert.Equal("test-agent", agent.Name);
        Assert.Equal("/tmp/work", agent.WorkingDirectory);
        Assert.Equal("pat-12345", agent.PersonalAccessToken);
        Assert.Equal(30, agent.PollingIntervalSeconds);
        Assert.Equal("gpt-4o", agent.Model);
        Assert.False(agent.AutoApproveTools);
        Assert.True(agent.DryRun);
        Assert.Equal("You are a helpful agent.", agent.SystemPrompt);
        Assert.Equal("https://example.com/repo.git", agent.RepositoryUrl);
        Assert.Equal("main", agent.RepositoryBranch);
        Assert.Equal("ghp_token123", agent.RepositoryAccessToken);
        Assert.Single(agent.McpServers);
        Assert.Equal("teamware", agent.McpServers[0].Name);
        Assert.Equal("http", agent.McpServers[0].Type);
        Assert.Equal("https://localhost:5001/mcp", agent.McpServers[0].Url);
        Assert.Equal("Bearer pat-12345", agent.McpServers[0].AuthHeader);
    }

    [Fact]
    public void Configuration_LoadsMultipleAgents()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("TestData/multiple-agents.json")
            .Build();

        var agents = new List<AgentIdentityOptions>();
        config.GetSection("Agents").Bind(agents);

        Assert.Equal(2, agents.Count);
        Assert.Equal("agent-one", agents[0].Name);
        Assert.Equal("agent-two", agents[1].Name);
        Assert.Equal("/tmp/work1", agents[0].WorkingDirectory);
        Assert.Equal("/tmp/work2", agents[1].WorkingDirectory);
    }

    [Fact]
    public void Configuration_EmptyAgents_LoadsEmptyList()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("TestData/empty-agents.json")
            .Build();

        var agents = new List<AgentIdentityOptions>();
        config.GetSection("Agents").Bind(agents);

        Assert.Empty(agents);
    }

    [Fact]
    public void Configuration_DefaultValues_AppliedForOmittedProperties()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("TestData/minimal-agent.json")
            .Build();

        var agents = new List<AgentIdentityOptions>();
        config.GetSection("Agents").Bind(agents);

        Assert.Single(agents);
        var agent = agents[0];
        Assert.Equal("minimal-agent", agent.Name);
        Assert.Equal("/tmp/work", agent.WorkingDirectory);
        Assert.Equal("pat-minimal", agent.PersonalAccessToken);
        Assert.Equal(60, agent.PollingIntervalSeconds);
        Assert.True(agent.AutoApproveTools);
        Assert.False(agent.DryRun);
        Assert.Equal(600, agent.TaskTimeoutSeconds);
        Assert.Null(agent.Model);
        Assert.Null(agent.SystemPrompt);
        Assert.Null(agent.RepositoryUrl);
        Assert.Null(agent.RepositoryBranch);
        Assert.Null(agent.RepositoryAccessToken);
    }

    // --- ResolveRepository Tests ---

    [Fact]
    public void ResolveRepository_NoRepositories_FallsBackToFlatFields()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            RepositoryUrl = "https://github.com/org/repo.git",
            RepositoryBranch = "develop",
            RepositoryAccessToken = "ghp_token"
        };

        var resolved = options.ResolveRepository("MyProject");

        Assert.Equal("https://github.com/org/repo.git", resolved.RepositoryUrl);
        Assert.Equal("develop", resolved.Branch);
        Assert.Equal("ghp_token", resolved.AccessToken);
        Assert.Equal(Path.Combine("/tmp/work", "default"), resolved.WorkingDirectory);
    }

    [Fact]
    public void ResolveRepository_NullProjectName_FallsBackToFlatFields()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            RepositoryUrl = "https://github.com/org/repo.git",
            Repositories =
            [
                new RepositoryOptions { ProjectName = "MyProject", Url = "https://github.com/org/other.git" }
            ]
        };

        var resolved = options.ResolveRepository(null);

        Assert.Equal("https://github.com/org/repo.git", resolved.RepositoryUrl);
        Assert.Equal(Path.Combine("/tmp/work", "default"), resolved.WorkingDirectory);
    }

    [Fact]
    public void ResolveRepository_MatchingProject_ReturnsProjectSpecificConfig()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            RepositoryUrl = "https://github.com/org/fallback.git",
            Repositories =
            [
                new RepositoryOptions
                {
                    ProjectName = "TeamWare",
                    Url = "https://github.com/org/teamware.git",
                    Branch = "feature-branch",
                    AccessToken = "ghp_project_token"
                }
            ]
        };

        var resolved = options.ResolveRepository("TeamWare");

        Assert.Equal("https://github.com/org/teamware.git", resolved.RepositoryUrl);
        Assert.Equal("feature-branch", resolved.Branch);
        Assert.Equal("ghp_project_token", resolved.AccessToken);
        Assert.Equal(Path.Combine("/tmp/work", "TeamWare"), resolved.WorkingDirectory);
    }

    [Fact]
    public void ResolveRepository_MatchIsCaseInsensitive()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            Repositories =
            [
                new RepositoryOptions { ProjectName = "MyProject", Url = "https://github.com/org/proj.git" }
            ]
        };

        var resolved = options.ResolveRepository("myproject");

        Assert.Equal("https://github.com/org/proj.git", resolved.RepositoryUrl);
        Assert.Contains("MyProject", resolved.WorkingDirectory);
    }

    [Fact]
    public void ResolveRepository_NoMatch_FallsBackToFlatFields()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            RepositoryUrl = "https://github.com/org/fallback.git",
            RepositoryBranch = "main",
            Repositories =
            [
                new RepositoryOptions { ProjectName = "ProjectA", Url = "https://github.com/org/a.git" }
            ]
        };

        var resolved = options.ResolveRepository("UnknownProject");

        Assert.Equal("https://github.com/org/fallback.git", resolved.RepositoryUrl);
        Assert.Equal("main", resolved.Branch);
        Assert.Equal(Path.Combine("/tmp/work", "default"), resolved.WorkingDirectory);
    }

    [Fact]
    public void ResolveRepository_MultipleRepositories_SelectsCorrectOne()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            Repositories =
            [
                new RepositoryOptions { ProjectName = "Frontend", Url = "https://github.com/org/frontend.git", Branch = "dev" },
                new RepositoryOptions { ProjectName = "Backend", Url = "https://github.com/org/backend.git", Branch = "staging" },
                new RepositoryOptions { ProjectName = "Docs", Url = "https://github.com/org/docs.git" }
            ]
        };

        var resolved = options.ResolveRepository("Backend");

        Assert.Equal("https://github.com/org/backend.git", resolved.RepositoryUrl);
        Assert.Equal("staging", resolved.Branch);
        Assert.Equal(Path.Combine("/tmp/work", "Backend"), resolved.WorkingDirectory);
    }

    [Fact]
    public void ResolveRepository_DefaultBranch_IsMain()
    {
        var options = new AgentIdentityOptions
        {
            WorkingDirectory = "/tmp/work",
            Repositories =
            [
                new RepositoryOptions { ProjectName = "Docs", Url = "https://github.com/org/docs.git" }
            ]
        };

        var resolved = options.ResolveRepository("Docs");

        Assert.Equal("main", resolved.Branch);
    }

    // --- SanitizeDirectoryName Tests ---

    [Theory]
    [InlineData("MyProject", "MyProject")]
    [InlineData("Project With Spaces", "Project With Spaces")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("trailing...", "trailing")]
    [InlineData("  padded  ", "padded")]
    [InlineData("col:on", "col_on")]
    public void SanitizeDirectoryName_HandlesEdgeCases(string input, string expected)
    {
        var result = AgentIdentityOptions.SanitizeDirectoryName(input);
        Assert.Equal(expected, result);
    }

    // --- RepositoryOptions Default Tests ---

    [Fact]
    public void RepositoryOptions_DefaultValues_AreApplied()
    {
        var options = new RepositoryOptions();

        Assert.Equal(string.Empty, options.ProjectName);
        Assert.Equal(string.Empty, options.Url);
        Assert.Equal("main", options.Branch);
        Assert.Null(options.AccessToken);
    }

    [Fact]
    public void Configuration_LoadsRepositoriesFromJson()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("TestData/multi-repo-agent.json")
            .Build();

        var agents = new List<AgentIdentityOptions>();
        config.GetSection("Agents").Bind(agents);

        Assert.Single(agents);
        var agent = agents[0];
        Assert.Equal("multi-repo-agent", agent.Name);
        Assert.Equal(2, agent.Repositories.Count);

        Assert.Equal("Frontend", agent.Repositories[0].ProjectName);
        Assert.Equal("https://github.com/org/frontend.git", agent.Repositories[0].Url);
        Assert.Equal("dev", agent.Repositories[0].Branch);
        Assert.Equal("ghp_frontend", agent.Repositories[0].AccessToken);

        Assert.Equal("Backend", agent.Repositories[1].ProjectName);
        Assert.Equal("https://github.com/org/backend.git", agent.Repositories[1].Url);
        Assert.Equal("main", agent.Repositories[1].Branch);
        Assert.Null(agent.Repositories[1].AccessToken);
    }

    // --- ApplyServerConfiguration Tests (SACFG-TEST-04, SACFG-TEST-05, SACFG-TEST-06) ---

    [Fact]
    public void ApplyServerConfiguration_NullConfig_DoesNothing()
    {
        var options = new AgentIdentityOptions
        {
            PollingIntervalSeconds = 60,
            Model = null,
            AutoApproveTools = true,
            DryRun = false,
            TaskTimeoutSeconds = 600,
            SystemPrompt = null
        };

        options.ApplyServerConfiguration(null);

        Assert.Equal(60, options.PollingIntervalSeconds);
        Assert.Null(options.Model);
        Assert.True(options.AutoApproveTools);
        Assert.False(options.DryRun);
        Assert.Equal(600, options.TaskTimeoutSeconds);
        Assert.Null(options.SystemPrompt);
    }

    [Fact]
    public void ApplyServerConfiguration_PollingInterval_AppliedWhenDefault()
    {
        var options = new AgentIdentityOptions { PollingIntervalSeconds = 60 };
        var config = new AgentProfileConfiguration { PollingIntervalSeconds = 30 };

        options.ApplyServerConfiguration(config);

        Assert.Equal(30, options.PollingIntervalSeconds);
    }

    [Fact]
    public void ApplyServerConfiguration_PollingInterval_NotOverriddenWhenNonDefault()
    {
        var options = new AgentIdentityOptions { PollingIntervalSeconds = 45 };
        var config = new AgentProfileConfiguration { PollingIntervalSeconds = 30 };

        options.ApplyServerConfiguration(config);

        Assert.Equal(45, options.PollingIntervalSeconds);
    }

    [Fact]
    public void ApplyServerConfiguration_PollingInterval_NotAppliedWhenServerNull()
    {
        var options = new AgentIdentityOptions { PollingIntervalSeconds = 60 };
        var config = new AgentProfileConfiguration { PollingIntervalSeconds = null };

        options.ApplyServerConfiguration(config);

        Assert.Equal(60, options.PollingIntervalSeconds);
    }

    [Fact]
    public void ApplyServerConfiguration_Model_AppliedWhenLocalNull()
    {
        var options = new AgentIdentityOptions { Model = null };
        var config = new AgentProfileConfiguration { Model = "gpt-4o" };

        options.ApplyServerConfiguration(config);

        Assert.Equal("gpt-4o", options.Model);
    }

    [Fact]
    public void ApplyServerConfiguration_Model_NotOverriddenWhenLocalSet()
    {
        var options = new AgentIdentityOptions { Model = "local-model" };
        var config = new AgentProfileConfiguration { Model = "server-model" };

        options.ApplyServerConfiguration(config);

        Assert.Equal("local-model", options.Model);
    }

    [Fact]
    public void ApplyServerConfiguration_AutoApproveTools_AppliedWhenDefault()
    {
        var options = new AgentIdentityOptions { AutoApproveTools = true };
        var config = new AgentProfileConfiguration { AutoApproveTools = false };

        options.ApplyServerConfiguration(config);

        Assert.False(options.AutoApproveTools);
    }

    [Fact]
    public void ApplyServerConfiguration_AutoApproveTools_NotOverriddenWhenNonDefault()
    {
        var options = new AgentIdentityOptions { AutoApproveTools = false };
        var config = new AgentProfileConfiguration { AutoApproveTools = true };

        options.ApplyServerConfiguration(config);

        Assert.False(options.AutoApproveTools);
    }

    [Fact]
    public void ApplyServerConfiguration_DryRun_AppliedWhenDefault()
    {
        var options = new AgentIdentityOptions { DryRun = false };
        var config = new AgentProfileConfiguration { DryRun = true };

        options.ApplyServerConfiguration(config);

        Assert.True(options.DryRun);
    }

    [Fact]
    public void ApplyServerConfiguration_DryRun_NotOverriddenWhenNonDefault()
    {
        var options = new AgentIdentityOptions { DryRun = true };
        var config = new AgentProfileConfiguration { DryRun = false };

        options.ApplyServerConfiguration(config);

        Assert.True(options.DryRun);
    }

    [Fact]
    public void ApplyServerConfiguration_TaskTimeout_AppliedWhenDefault()
    {
        var options = new AgentIdentityOptions { TaskTimeoutSeconds = 600 };
        var config = new AgentProfileConfiguration { TaskTimeoutSeconds = 1200 };

        options.ApplyServerConfiguration(config);

        Assert.Equal(1200, options.TaskTimeoutSeconds);
    }

    [Fact]
    public void ApplyServerConfiguration_TaskTimeout_NotOverriddenWhenNonDefault()
    {
        var options = new AgentIdentityOptions { TaskTimeoutSeconds = 300 };
        var config = new AgentProfileConfiguration { TaskTimeoutSeconds = 1200 };

        options.ApplyServerConfiguration(config);

        Assert.Equal(300, options.TaskTimeoutSeconds);
    }

    [Fact]
    public void ApplyServerConfiguration_SystemPrompt_AppliedWhenLocalNull()
    {
        var options = new AgentIdentityOptions { SystemPrompt = null };
        var config = new AgentProfileConfiguration { SystemPrompt = "Server prompt" };

        options.ApplyServerConfiguration(config);

        Assert.Equal("Server prompt", options.SystemPrompt);
    }

    [Fact]
    public void ApplyServerConfiguration_SystemPrompt_NotOverriddenWhenLocalSet()
    {
        var options = new AgentIdentityOptions { SystemPrompt = "Local prompt" };
        var config = new AgentProfileConfiguration { SystemPrompt = "Server prompt" };

        options.ApplyServerConfiguration(config);

        Assert.Equal("Local prompt", options.SystemPrompt);
    }

    [Fact]
    public void ApplyServerConfiguration_FlatRepoFields_AppliedWhenLocalNull()
    {
        var options = new AgentIdentityOptions
        {
            RepositoryUrl = null,
            RepositoryBranch = null,
            RepositoryAccessToken = null
        };
        var config = new AgentProfileConfiguration
        {
            RepositoryUrl = "https://github.com/org/repo.git",
            RepositoryBranch = "develop",
            RepositoryAccessToken = "ghp_server_token"
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal("https://github.com/org/repo.git", options.RepositoryUrl);
        Assert.Equal("develop", options.RepositoryBranch);
        Assert.Equal("ghp_server_token", options.RepositoryAccessToken);
    }

    [Fact]
    public void ApplyServerConfiguration_FlatRepoFields_NotOverriddenWhenLocalSet()
    {
        var options = new AgentIdentityOptions
        {
            RepositoryUrl = "https://github.com/org/local.git",
            RepositoryBranch = "main",
            RepositoryAccessToken = "ghp_local"
        };
        var config = new AgentProfileConfiguration
        {
            RepositoryUrl = "https://github.com/org/server.git",
            RepositoryBranch = "develop",
            RepositoryAccessToken = "ghp_server"
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal("https://github.com/org/local.git", options.RepositoryUrl);
        Assert.Equal("main", options.RepositoryBranch);
        Assert.Equal("ghp_local", options.RepositoryAccessToken);
    }

    [Fact]
    public void ApplyServerConfiguration_Repositories_ServerOnlyAppended()
    {
        var options = new AgentIdentityOptions
        {
            Repositories =
            [
                new RepositoryOptions { ProjectName = "LocalProject", Url = "https://local.git" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            Repositories =
            [
                new AgentProfileRepository { ProjectName = "ServerProject", Url = "https://server.git", Branch = "dev", AccessToken = "tok" }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal(2, options.Repositories.Count);
        Assert.Equal("LocalProject", options.Repositories[0].ProjectName);
        Assert.Equal("ServerProject", options.Repositories[1].ProjectName);
        Assert.Equal("https://server.git", options.Repositories[1].Url);
        Assert.Equal("dev", options.Repositories[1].Branch);
        Assert.Equal("tok", options.Repositories[1].AccessToken);
    }

    [Fact]
    public void ApplyServerConfiguration_Repositories_LocalWinsOnCollision()
    {
        var options = new AgentIdentityOptions
        {
            Repositories =
            [
                new RepositoryOptions { ProjectName = "Shared", Url = "https://local.git", Branch = "local-branch" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            Repositories =
            [
                new AgentProfileRepository { ProjectName = "Shared", Url = "https://server.git", Branch = "server-branch" }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Single(options.Repositories);
        Assert.Equal("https://local.git", options.Repositories[0].Url);
        Assert.Equal("local-branch", options.Repositories[0].Branch);
    }

    [Fact]
    public void ApplyServerConfiguration_Repositories_CaseInsensitiveMatch()
    {
        var options = new AgentIdentityOptions
        {
            Repositories =
            [
                new RepositoryOptions { ProjectName = "MyProject", Url = "https://local.git" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            Repositories =
            [
                new AgentProfileRepository { ProjectName = "myproject", Url = "https://server.git" }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Single(options.Repositories);
        Assert.Equal("https://local.git", options.Repositories[0].Url);
    }

    [Fact]
    public void ApplyServerConfiguration_Repositories_NoLocalRepos_ServerReposUsed()
    {
        var options = new AgentIdentityOptions();
        var config = new AgentProfileConfiguration
        {
            Repositories =
            [
                new AgentProfileRepository { ProjectName = "A", Url = "https://a.git" },
                new AgentProfileRepository { ProjectName = "B", Url = "https://b.git" }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal(2, options.Repositories.Count);
        Assert.Equal("A", options.Repositories[0].ProjectName);
        Assert.Equal("B", options.Repositories[1].ProjectName);
    }

    [Fact]
    public void ApplyServerConfiguration_Repositories_NullServerList_NoChange()
    {
        var options = new AgentIdentityOptions
        {
            Repositories =
            [
                new RepositoryOptions { ProjectName = "Local", Url = "https://local.git" }
            ]
        };
        var config = new AgentProfileConfiguration { Repositories = null };

        options.ApplyServerConfiguration(config);

        Assert.Single(options.Repositories);
    }

    [Fact]
    public void ApplyServerConfiguration_McpServers_ServerOnlyAppended()
    {
        var options = new AgentIdentityOptions
        {
            McpServers =
            [
                new McpServerOptions { Name = "local-mcp", Type = "http", Url = "https://local-mcp" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            McpServers =
            [
                new AgentProfileMcpServer
                {
                    Name = "server-mcp", Type = "stdio", Command = "npx",
                    Args = ["--yes", "server-tool"], Env = new Dictionary<string, string> { ["KEY"] = "VAL" }
                }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal(2, options.McpServers.Count);
        Assert.Equal("local-mcp", options.McpServers[0].Name);
        Assert.Equal("server-mcp", options.McpServers[1].Name);
        Assert.Equal("stdio", options.McpServers[1].Type);
        Assert.Equal("npx", options.McpServers[1].Command);
        Assert.Equal(["--yes", "server-tool"], options.McpServers[1].Args);
        Assert.Equal("VAL", options.McpServers[1].Env["KEY"]);
    }

    [Fact]
    public void ApplyServerConfiguration_McpServers_LocalWinsOnCollision()
    {
        var options = new AgentIdentityOptions
        {
            McpServers =
            [
                new McpServerOptions { Name = "teamware", Type = "http", Url = "https://local" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            McpServers =
            [
                new AgentProfileMcpServer { Name = "teamware", Type = "http", Url = "https://server" }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Single(options.McpServers);
        Assert.Equal("https://local", options.McpServers[0].Url);
    }

    [Fact]
    public void ApplyServerConfiguration_McpServers_CaseInsensitiveMatch()
    {
        var options = new AgentIdentityOptions
        {
            McpServers =
            [
                new McpServerOptions { Name = "TeamWare", Type = "http", Url = "https://local" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            McpServers =
            [
                new AgentProfileMcpServer { Name = "teamware", Type = "http", Url = "https://server" }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Single(options.McpServers);
        Assert.Equal("https://local", options.McpServers[0].Url);
    }

    [Fact]
    public void ApplyServerConfiguration_McpServers_NullArgsAndEnv_DefaultToEmpty()
    {
        var options = new AgentIdentityOptions();
        var config = new AgentProfileConfiguration
        {
            McpServers =
            [
                new AgentProfileMcpServer { Name = "test", Type = "stdio", Command = "cmd", Args = null, Env = null }
            ]
        };

        options.ApplyServerConfiguration(config);

        Assert.Single(options.McpServers);
        Assert.Empty(options.McpServers[0].Args);
        Assert.Empty(options.McpServers[0].Env);
    }

    [Fact]
    public void ApplyServerConfiguration_WorkingDirectory_NeverOverwritten()
    {
        var options = new AgentIdentityOptions { WorkingDirectory = "/tmp/local" };
        var config = new AgentProfileConfiguration();

        options.ApplyServerConfiguration(config);

        Assert.Equal("/tmp/local", options.WorkingDirectory);
    }

    [Fact]
    public void ApplyServerConfiguration_PersonalAccessToken_NeverOverwritten()
    {
        var options = new AgentIdentityOptions { PersonalAccessToken = "local-pat" };
        var config = new AgentProfileConfiguration();

        options.ApplyServerConfiguration(config);

        Assert.Equal("local-pat", options.PersonalAccessToken);
    }

    [Fact]
    public void ApplyServerConfiguration_AllBehavioralFieldsAtDefault_AllApplied()
    {
        var options = new AgentIdentityOptions();
        var config = new AgentProfileConfiguration
        {
            PollingIntervalSeconds = 15,
            Model = "gpt-4o",
            AutoApproveTools = false,
            DryRun = true,
            TaskTimeoutSeconds = 1800,
            SystemPrompt = "Be concise."
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal(15, options.PollingIntervalSeconds);
        Assert.Equal("gpt-4o", options.Model);
        Assert.False(options.AutoApproveTools);
        Assert.True(options.DryRun);
        Assert.Equal(1800, options.TaskTimeoutSeconds);
        Assert.Equal("Be concise.", options.SystemPrompt);
    }

    [Fact]
    public void ApplyServerConfiguration_AllBehavioralFieldsNonDefault_NoneApplied()
    {
        var options = new AgentIdentityOptions
        {
            PollingIntervalSeconds = 45,
            Model = "local-model",
            AutoApproveTools = false,
            DryRun = true,
            TaskTimeoutSeconds = 300,
            SystemPrompt = "Local prompt"
        };
        var config = new AgentProfileConfiguration
        {
            PollingIntervalSeconds = 15,
            Model = "server-model",
            AutoApproveTools = true,
            DryRun = false,
            TaskTimeoutSeconds = 1800,
            SystemPrompt = "Server prompt"
        };

        options.ApplyServerConfiguration(config);

        Assert.Equal(45, options.PollingIntervalSeconds);
        Assert.Equal("local-model", options.Model);
        Assert.False(options.AutoApproveTools);
        Assert.True(options.DryRun);
        Assert.Equal(300, options.TaskTimeoutSeconds);
        Assert.Equal("Local prompt", options.SystemPrompt);
    }

    [Fact]
    public void ApplyServerConfiguration_MixedReposAndMcpServers_MergedCorrectly()
    {
        var options = new AgentIdentityOptions
        {
            Repositories =
            [
                new RepositoryOptions { ProjectName = "Shared", Url = "https://local-shared.git" },
                new RepositoryOptions { ProjectName = "LocalOnly", Url = "https://local-only.git" }
            ],
            McpServers =
            [
                new McpServerOptions { Name = "shared-mcp", Type = "http", Url = "https://local-mcp" },
                new McpServerOptions { Name = "local-only-mcp", Type = "stdio", Command = "local-cmd" }
            ]
        };
        var config = new AgentProfileConfiguration
        {
            Repositories =
            [
                new AgentProfileRepository { ProjectName = "Shared", Url = "https://server-shared.git" },
                new AgentProfileRepository { ProjectName = "ServerOnly", Url = "https://server-only.git" }
            ],
            McpServers =
            [
                new AgentProfileMcpServer { Name = "shared-mcp", Type = "http", Url = "https://server-mcp" },
                new AgentProfileMcpServer { Name = "server-only-mcp", Type = "http", Url = "https://server-only-mcp" }
            ]
        };

        options.ApplyServerConfiguration(config);

        // Repos: 2 local + 1 server-only (Shared collision: local wins)
        Assert.Equal(3, options.Repositories.Count);
        Assert.Equal("https://local-shared.git", options.Repositories.First(r => r.ProjectName == "Shared").Url);
        Assert.Contains(options.Repositories, r => r.ProjectName == "LocalOnly");
        Assert.Contains(options.Repositories, r => r.ProjectName == "ServerOnly");

        // McpServers: 2 local + 1 server-only (shared-mcp collision: local wins)
        Assert.Equal(3, options.McpServers.Count);
        Assert.Equal("https://local-mcp", options.McpServers.First(s => s.Name == "shared-mcp").Url);
        Assert.Contains(options.McpServers, s => s.Name == "local-only-mcp");
        Assert.Contains(options.McpServers, s => s.Name == "server-only-mcp");
    }
}
