using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Tests;

public class AgentHostedServiceTests
{
    private static IOptions<List<AgentIdentityOptions>> CreateOptions(List<AgentIdentityOptions>? agents = null)
    {
        return Options.Create(agents ?? []);
    }

    private static AgentHostedService CreateService(
        List<AgentIdentityOptions>? agents = null,
        TestLogger<AgentHostedService>? logger = null,
        ITeamWareMcpClientFactory? factory = null)
    {
        logger ??= new TestLogger<AgentHostedService>();
        factory ??= new FakeMcpClientFactory();
        var loggerFactory = new TestLoggerFactory(logger);

        return new AgentHostedService(CreateOptions(agents), factory, loggerFactory, logger);
    }

    [Fact]
    public async Task StartAsync_NoAgents_LogsWarning()
    {
        var logger = new TestLogger<AgentHostedService>();
        var service = CreateService(logger: logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("No agent identities configured"));
    }

    [Fact]
    public async Task StartAsync_WithAgents_LogsAgentCount()
    {
        var agents = new List<AgentIdentityOptions>
        {
            new() { Name = "agent-1", PollingIntervalSeconds = 1 },
            new() { Name = "agent-2", PollingIntervalSeconds = 1 }
        };
        var logger = new TestLogger<AgentHostedService>();
        var service = CreateService(agents, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("2 configured identity(ies)"));
    }

    [Fact]
    public async Task StartAsync_StartsOneLoopPerIdentity()
    {
        var agents = new List<AgentIdentityOptions>
        {
            new() { Name = "agent-alpha", PollingIntervalSeconds = 1 },
            new() { Name = "agent-beta", PollingIntervalSeconds = 1 }
        };
        var logger = new TestLogger<AgentHostedService>();
        var service = CreateService(agents, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("'agent-alpha'") &&
            e.Message.Contains("Starting polling loop"));
        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("'agent-beta'") &&
            e.Message.Contains("Starting polling loop"));
    }

    [Fact]
    public async Task StopAsync_CancelsAllLoops()
    {
        var agents = new List<AgentIdentityOptions>
        {
            new() { Name = "agent-stop", PollingIntervalSeconds = 60 }
        };
        var logger = new TestLogger<AgentHostedService>();
        var service = CreateService(agents, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Polling loop stopped") &&
            e.Message.Contains("'agent-stop'"));
    }

    [Fact]
    public async Task StartAndStop_CompletesCleanly()
    {
        var agents = new List<AgentIdentityOptions>
        {
            new() { Name = "clean-agent", PollingIntervalSeconds = 60 }
        };
        var logger = new TestLogger<AgentHostedService>();
        var service = CreateService(agents, logger);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("TeamWare Agent starting"));
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("TeamWare Agent stopped"));
    }

    [Fact]
    public async Task StartAsync_NoAgents_CompletesWithoutStartingLoops()
    {
        var logger = new TestLogger<AgentHostedService>();
        var service = CreateService(logger: logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Starting polling loop"));
    }

    [Fact]
    public async Task StartAsync_McpClientCreationFails_LogsErrorAndContinues()
    {
        var agents = new List<AgentIdentityOptions>
        {
            new() { Name = "failing-agent", PollingIntervalSeconds = 1 }
        };
        var logger = new TestLogger<AgentHostedService>();
        var factory = new FakeMcpClientFactory { ThrowOnCreate = true };
        var service = CreateService(agents, logger, factory);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Failed to create MCP client") &&
            e.Message.Contains("'failing-agent'"));
    }
}
