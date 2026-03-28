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
        Assert.Null(agent.Model);
        Assert.Null(agent.SystemPrompt);
        Assert.Null(agent.RepositoryUrl);
        Assert.Null(agent.RepositoryBranch);
        Assert.Null(agent.RepositoryAccessToken);
    }
}
