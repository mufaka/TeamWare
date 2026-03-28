using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Repository;

namespace TeamWare.Agent.Pipeline;

public class AgentPollingLoop
{
    private readonly AgentIdentityOptions _options;
    private readonly ITeamWareMcpClient _mcpClient;
    private readonly ICopilotClientWrapperFactory? _copilotFactory;
    private readonly StatusTransitionHandler _statusHandler;
    private readonly RepositoryManager _repoManager;
    private readonly ILogger _logger;

    public AgentPollingLoop(
        AgentIdentityOptions options,
        ITeamWareMcpClient mcpClient,
        ILogger logger)
        : this(options, mcpClient, copilotFactory: null, logger)
    {
    }

    public AgentPollingLoop(
        AgentIdentityOptions options,
        ITeamWareMcpClient mcpClient,
        ICopilotClientWrapperFactory? copilotFactory,
        ILogger logger)
    {
        _options = options;
        _mcpClient = mcpClient;
        _copilotFactory = copilotFactory;
        _statusHandler = new StatusTransitionHandler(mcpClient, logger);
        _repoManager = new RepositoryManager(logger);
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

        // Step 3: Process tasks one at a time (CA-21, CA-34)
        foreach (var task in todoTasks)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessTaskAsync(task, cancellationToken);
        }
    }

    private async Task ProcessTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (_copilotFactory is null)
        {
            // No Copilot factory configured — log placeholder (useful for testing/Phase 38 compat)
            _logger.LogInformation(
                "Agent '{AgentName}': Would process task #{TaskId}: {TaskTitle} (Project: {ProjectName})",
                _options.Name, task.Id, task.Title, task.ProjectName);
            return;
        }

        // Read-before-write: verify current task state before processing (CA-100)
        var currentTask = await _mcpClient.GetTaskAsync(task.Id, cancellationToken);
        if (!currentTask.Status.Equals("ToDo", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotency: skip tasks not in ToDo status (CA-NF-06)
            _logger.LogInformation(
                "Agent '{AgentName}': Skipping task #{TaskId} — status is '{Status}', expected 'ToDo'",
                _options.Name, task.Id, currentTask.Status);
            return;
        }

        // Pick up: comment + transition to InProgress (CA-60, CA-65)
        await _statusHandler.PickUpTaskAsync(task.Id, cancellationToken);

        try
        {
            // Ensure repository is up to date before processing (CA-50 through CA-54)
            await _repoManager.EnsureRepositoryAsync(_options, cancellationToken);

            var processor = new TaskProcessor(_options, _copilotFactory, _mcpClient, _logger);
            await processor.ProcessAsync(task, cancellationToken);

            // Success: comment + transition to InReview (CA-61, CA-70)
            await _statusHandler.CompleteTaskAsync(
                task.Id,
                $"I've completed work on this task and moved it to review.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Task-level error — error comment + Error status + lounge message (CA-140, CA-141, CA-142)
            _logger.LogError(ex,
                "Agent '{AgentName}': Error processing task #{TaskId}: {TaskTitle}",
                _options.Name, task.Id, task.Title);

            try
            {
                await _statusHandler.ErrorTaskAsync(
                    task.Id,
                    $"An error occurred while processing this task: {ex.Message}",
                    task.Title,
                    task.ProjectId,
                    cancellationToken);
            }
            catch (Exception statusEx)
            {
                _logger.LogError(statusEx,
                    "Agent '{AgentName}': Failed to transition task #{TaskId} to Error status",
                    _options.Name, task.Id);
            }
        }
    }
}
