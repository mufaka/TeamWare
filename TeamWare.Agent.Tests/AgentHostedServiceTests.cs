using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Tests;

public class AgentHostedServiceTests
{
    private static IOptions<List<AgentIdentityOptions>> CreateOptions(List<AgentIdentityOptions>? agents = null)
    {
        return Options.Create(agents ?? []);
    }

    [Fact]
    public async Task StartAsync_NoAgents_LogsWarning()
    {
        var logger = new TestLogger<AgentHostedService>();
        var service = new AgentHostedService(CreateOptions(), logger);

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
        var service = new AgentHostedService(CreateOptions(agents), logger);

        await service.StartAsync(CancellationToken.None);
        // Give the polling loops a moment to start
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
        var service = new AgentHostedService(CreateOptions(agents), logger);

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
        var service = new AgentHostedService(CreateOptions(agents), logger);

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
        var service = new AgentHostedService(CreateOptions(agents), logger);

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
        var service = new AgentHostedService(CreateOptions(), logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("Starting polling loop"));
    }
}
