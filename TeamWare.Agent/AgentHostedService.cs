using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Pipeline;

namespace TeamWare.Agent;

public class AgentHostedService : IHostedService
{
    private readonly List<AgentIdentityOptions> _agents;
    private readonly ITeamWareMcpClientFactory _mcpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AgentHostedService> _logger;
    private readonly List<Task> _pollingTasks = [];
    private CancellationTokenSource? _cts;

    public AgentHostedService(
        IOptions<List<AgentIdentityOptions>> agents,
        ITeamWareMcpClientFactory mcpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<AgentHostedService> logger)
    {
        _agents = agents.Value;
        _mcpClientFactory = mcpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TeamWare Agent starting with {AgentCount} configured identity(ies)", _agents.Count);

        if (_agents.Count == 0)
        {
            _logger.LogWarning("No agent identities configured. The agent will idle until configuration is provided");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        foreach (var agent in _agents)
        {
            _logger.LogInformation("Starting polling loop for agent identity '{AgentName}'", agent.Name);
            var task = Task.Run(() => RunPollingLoopAsync(agent, _cts.Token), _cts.Token);
            _pollingTasks.Add(task);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TeamWare Agent shutting down");

        if (_cts is not null)
        {
            await _cts.CancelAsync();

            try
            {
                await Task.WhenAll(_pollingTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }

            _cts.Dispose();
        }

        _logger.LogInformation("TeamWare Agent stopped");
    }

    private async Task RunPollingLoopAsync(AgentIdentityOptions agent, CancellationToken cancellationToken)
    {
        ITeamWareMcpClient? mcpClient = null;

        try
        {
            mcpClient = await _mcpClientFactory.CreateAsync(agent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to create MCP client for agent identity '{AgentName}'. Polling loop will not start",
                agent.Name);
            return;
        }

        try
        {
            var pollingLoop = new AgentPollingLoop(
                agent,
                mcpClient,
                _loggerFactory.CreateLogger<AgentPollingLoop>());

            await pollingLoop.RunAsync(cancellationToken);
        }
        finally
        {
            if (mcpClient is not null)
            {
                await mcpClient.DisposeAsync();
            }
        }
    }
}
