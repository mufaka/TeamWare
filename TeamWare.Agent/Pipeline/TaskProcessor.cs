using System.Text;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Pipeline;

/// <summary>
/// Processes a single task by creating a Copilot SDK session and sending
/// the task context as a prompt. The LLM reasons and acts using all available
/// tools (built-in CLI tools + MCP servers).
/// See Specification Section 3.5 (CA-40 through CA-45).
/// </summary>
public class TaskProcessor
{
    private readonly AgentIdentityOptions _options;
    private readonly ICopilotClientWrapperFactory _clientFactory;
    private readonly ITeamWareMcpClient _mcpClient;
    private readonly ILogger _logger;

    public TaskProcessor(
        AgentIdentityOptions options,
        ICopilotClientWrapperFactory clientFactory,
        ITeamWareMcpClient mcpClient,
        ILogger logger)
    {
        _options = options;
        _clientFactory = clientFactory;
        _mcpClient = mcpClient;
        _logger = logger;
    }

    /// <summary>
    /// Processes a single task by creating a Copilot session and sending the task prompt.
    /// </summary>
    public async Task ProcessAsync(AgentTask task, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing task #{TaskId}: {TaskTitle} for agent '{AgentName}'",
            task.Id, task.Title, _options.Name);

        // Fetch full task details including description and comments (CA-42)
        var taskDetail = await _mcpClient.GetTaskAsync(task.Id, cancellationToken);

        // Create the Copilot client (CA-44)
        await using var client = _clientFactory.Create(_options, _logger);
        await client.StartAsync();

        // Build session configuration (CA-41)
        var sessionConfig = BuildSessionConfig();

        // Create session (CA-40)
        await using var session = await client.CreateSessionAsync(sessionConfig);

        // Construct and send the task prompt (CA-42)
        var prompt = BuildTaskPrompt(taskDetail, task.ProjectName);

        _logger.LogDebug(
            "Sending task prompt for task #{TaskId} to Copilot session",
            task.Id);

        // Send prompt and wait for the session to complete (CA-42)
        await session.SendAndWaitAsync(prompt, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Copilot session completed for task #{TaskId}: {TaskTitle}",
            task.Id, task.Title);
    }

    /// <summary>
    /// Builds the SessionConfig for the Copilot session.
    /// </summary>
    internal SessionConfig BuildSessionConfig()
    {
        var systemPromptText = string.IsNullOrWhiteSpace(_options.SystemPrompt)
            ? DefaultSystemPrompt.Text
            : _options.SystemPrompt;

        var config = new SessionConfig
        {
            OnPermissionRequest = _options.AutoApproveTools
                ? PermissionHandler.ApproveAll
                : CreateDefaultPermissionHandler(),
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = systemPromptText
            }
        };

        // Set model if configured (CA-41)
        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            config.Model = _options.Model;
        }

        return config;
    }

    /// <summary>
    /// Builds the task prompt containing all relevant task information.
    /// See CA-42.
    /// </summary>
    internal static string BuildTaskPrompt(AgentTaskDetail taskDetail, string? projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You have been assigned the following task. Please complete it according to your instructions.");
        sb.AppendLine();
        sb.AppendLine($"Task ID: {taskDetail.Id}");
        sb.AppendLine($"Title: {taskDetail.Title}");
        sb.AppendLine($"Project: {projectName ?? "Unknown"}");
        sb.AppendLine($"Priority: {taskDetail.Priority}");
        sb.AppendLine($"Status: {taskDetail.Status}");

        if (!string.IsNullOrWhiteSpace(taskDetail.Description))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(taskDetail.Description);
        }

        if (taskDetail.Comments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Existing Comments:");
            foreach (var comment in taskDetail.Comments)
            {
                sb.AppendLine($"  [{comment.CreatedAt}] {comment.AuthorName}: {comment.Content}");
            }
        }

        return sb.ToString();
    }

    private PermissionRequestHandler CreateDefaultPermissionHandler()
    {
        return (request, invocation) =>
        {
            _logger.LogDebug(
                "Permission request: Kind={Kind}",
                request.Kind);

            return Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.Approved
            });
        };
    }
}
