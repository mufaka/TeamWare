using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Pipeline;

public class AgentPollingLoop
{
    private readonly AgentIdentityOptions _options;
    private readonly ITeamWareMcpClient _mcpClient;
    private readonly ILogger _logger;

    public AgentPollingLoop(
        AgentIdentityOptions options,
        ITeamWareMcpClient mcpClient,
        ILogger logger)
    {
        _options = options;
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Polling loop started for agent identity '{AgentName}'", _options.Name);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteCycleAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in polling cycle for agent identity '{AgentName}'", _options.Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Polling loop stopped for agent identity '{AgentName}'", _options.Name);
    }

    internal async Task ExecuteCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Polling cycle starting for '{AgentName}'", _options.Name);

        // Step 1: Check agent profile and active status
        var profile = await _mcpClient.GetMyProfileAsync(cancellationToken);

        if (profile.IsAgentActive != true)
        {
            _logger.LogWarning(
                "Agent '{AgentName}' is not active (isAgentActive={IsActive}). Skipping this cycle",
                _options.Name, profile.IsAgentActive);
            return;
        }

        // Step 2: Get assignments and filter to ToDo only
        var assignments = await _mcpClient.GetMyAssignmentsAsync(cancellationToken);
        var todoTasks = assignments
            .Where(t => t.Status.Equals("ToDo", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (todoTasks.Count == 0)
        {
            _logger.LogInformation(
                "Agent '{AgentName}': No ToDo tasks found ({TotalCount} total assignments)",
                _options.Name, assignments.Count);
            return;
        }

        _logger.LogInformation(
            "Agent '{AgentName}': Found {TodoCount} ToDo task(s) out of {TotalCount} total assignments",
            _options.Name, todoTasks.Count, assignments.Count);

        // Step 3: Process tasks one at a time (actual processing in Phase 39)
        foreach (var task in todoTasks)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            _logger.LogInformation(
                "Agent '{AgentName}': Would process task #{TaskId}: {TaskTitle} (Project: {ProjectName})",
                _options.Name, task.Id, task.Title, task.ProjectName);
        }
    }
}
