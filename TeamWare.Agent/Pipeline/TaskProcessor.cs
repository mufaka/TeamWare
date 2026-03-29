using System.Text;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using TeamWare.Agent.Configuration;
using TeamWare.Agent.Logging;
using TeamWare.Agent.Mcp;
using TeamWare.Agent.Permissions;

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
        var timeout = TimeSpan.FromSeconds(_options.TaskTimeoutSeconds);
        await session.SendAndWaitAsync(prompt, timeout, cancellationToken);

        _logger.LogInformation(
            "Copilot session completed for task #{TaskId}: {TaskTitle}",
            task.Id, task.Title);
    }

    /// <summary>
    /// Builds the SessionConfig for the Copilot session.
    /// When DryRun is enabled, write operations are blocked and logged via DryRunLogger (CA-120, CA-121).
    /// When AutoApproveTools is false, uses AgentPermissionHandler for safety guardrails (CA-131).
    /// When AutoApproveTools is true (and not dry run), uses PermissionHandler.ApproveAll (CA-130).
    /// </summary>
    internal SessionConfig BuildSessionConfig()
    {
        var systemPromptText = string.IsNullOrWhiteSpace(_options.SystemPrompt)
            ? DefaultSystemPrompt.Text
            : _options.SystemPrompt;

        var config = new SessionConfig
        {
            OnPermissionRequest = CreatePermissionHandler(),
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

        // Configure MCP servers for the Copilot session (CA-41)
        var mcpServers = BuildMcpServersDictionary();
        if (mcpServers.Count > 0)
        {
            config.McpServers = mcpServers;
        }

        return config;
    }

    /// <summary>
    /// Creates the appropriate permission handler based on configuration.
    /// Priority: DryRun > AutoApproveTools=false > ApproveAll
    /// DryRun mode is independent of AutoApproveTools (CA-132).
    /// </summary>
    internal PermissionRequestHandler CreatePermissionHandler()
    {
        if (_options.DryRun)
        {
            _logger.LogInformation(
                "Dry run mode enabled for agent '{AgentName}' — write operations will be logged, not executed",
                _options.Name);
            var dryRunLogger = new DryRunLogger(_logger);
            return dryRunLogger.CreateHandler();
        }

        if (!_options.AutoApproveTools)
        {
            _logger.LogInformation(
                "Custom permission handler enabled for agent '{AgentName}'",
                _options.Name);
            var permissionHandler = new AgentPermissionHandler(_logger);
            return permissionHandler.CreateHandler();
        }

        return PermissionHandler.ApproveAll;
    }

    /// <summary>
    /// Builds the MCP servers dictionary for the Copilot session configuration.
    /// HTTP servers use McpRemoteServerConfig; local/stdio servers use McpLocalServerConfig.
    /// </summary>
    internal Dictionary<string, object> BuildMcpServersDictionary()
    {
        var servers = new Dictionary<string, object>();

        foreach (var mcp in _options.McpServers)
        {
            if (mcp.Type.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                var httpConfig = new McpRemoteServerConfig
                {
                    Type = "http",
                    Url = mcp.Url ?? string.Empty,
                    Tools = new List<string> { "*" }
                };

                if (!string.IsNullOrEmpty(mcp.AuthHeader))
                {
                    httpConfig.Headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = mcp.AuthHeader
                    };
                }
                else if (!string.IsNullOrEmpty(_options.PersonalAccessToken))
                {
                    httpConfig.Headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {_options.PersonalAccessToken}"
                    };
                }

                servers[mcp.Name] = httpConfig;
            }
            else if (mcp.Type.Equals("stdio", StringComparison.OrdinalIgnoreCase)
                     || mcp.Type.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                var localConfig = new McpLocalServerConfig
                {
                    Type = "local",
                    Command = mcp.Command ?? string.Empty,
                    Args = mcp.Args,
                    Tools = new List<string> { "*" }
                };

                if (mcp.Env.Count > 0)
                {
                    localConfig.Env = mcp.Env;
                }

                servers[mcp.Name] = localConfig;
            }
            else
            {
                _logger.LogWarning(
                    "Unknown MCP server type '{Type}' for server '{Name}', skipping",
                    mcp.Type, mcp.Name);
            }
        }

        return servers;
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

    }
