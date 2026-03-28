using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent;

public class AgentHostedService : IHostedService
{
    private readonly List<AgentIdentityOptions> _agents;
    private readonly ILogger<AgentHostedService> _logger;
    private readonly List<Task> _pollingTasks = [];
    private CancellationTokenSource? _cts;

    public AgentHostedService(
        IOptions<List<AgentIdentityOptions>> agents,
        ILogger<AgentHostedService> logger)
    {
        _agents = agents.Value;
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
        _logger.LogInformation("Polling loop started for agent identity '{AgentName}'", agent.Name);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Polling cycle for '{AgentName}'", agent.Name);
                // Placeholder: actual polling logic will be added in Phase 38
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in polling cycle for agent identity '{AgentName}'", agent.Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(agent.PollingIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Polling loop stopped for agent identity '{AgentName}'", agent.Name);
    }
}
