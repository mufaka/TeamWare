using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamWare.Agent.Configuration;

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
        Assert.Equal("/tmp/work", resolved.WorkingDirectory);
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
        Assert.Equal("/tmp/work", resolved.WorkingDirectory);
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
        Assert.Equal("/tmp/work", resolved.WorkingDirectory);
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
}
