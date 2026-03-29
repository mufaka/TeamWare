using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TeamWare.Agent.Configuration;

namespace TeamWare.Agent.Mcp;

public class TeamWareMcpClient : ITeamWareMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly McpClient _mcpClient;
    private readonly ILogger<TeamWareMcpClient> _logger;

    private TeamWareMcpClient(McpClient mcpClient, ILogger<TeamWareMcpClient> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public static async Task<TeamWareMcpClient> CreateAsync(
        AgentIdentityOptions options,
        ILogger<TeamWareMcpClient> logger,
        CancellationToken cancellationToken = default)
    {
        var mcpServer = options.McpServers.FirstOrDefault(s =>
            s.Type.Equals("http", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No HTTP MCP server configured for agent '{options.Name}'.");

        if (string.IsNullOrEmpty(mcpServer.Url))
        {
            throw new InvalidOperationException(
                $"MCP server URL is required for agent '{options.Name}'.");
        }

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(mcpServer.Url),
            TransportMode = HttpTransportMode.StreamableHttp
        };

        if (!string.IsNullOrEmpty(mcpServer.AuthHeader))
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = mcpServer.AuthHeader
            };
        }
        else if (!string.IsNullOrEmpty(options.PersonalAccessToken))
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {options.PersonalAccessToken}"
            };
        }

        var transport = new HttpClientTransport(transportOptions);
        var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        logger.LogDebug("MCP client connected to {Url} for agent '{AgentName}'",
            mcpServer.Url, options.Name);

        return new TeamWareMcpClient(mcpClient, logger);
    }

    public async Task<AgentProfile> GetMyProfileAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallToolAsync("get_my_profile", null, cancellationToken);
        return DeserializeResult<AgentProfile>(result, "get_my_profile");
    }

    public async Task<IReadOnlyList<AgentTask>> GetMyAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallToolAsync("my_assignments", null, cancellationToken);
        return DeserializeResult<List<AgentTask>>(result, "my_assignments");
    }

    public async Task<AgentTaskDetail> GetTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["taskId"] = taskId };
        var result = await CallToolAsync("get_task", args, cancellationToken);
        return DeserializeResult<AgentTaskDetail>(result, "get_task");
    }

    public async Task UpdateTaskStatusAsync(int taskId, string status, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["taskId"] = taskId,
            ["status"] = status
        };
        await CallToolAsync("update_task_status", args, cancellationToken);
    }

    public async Task AddCommentAsync(int taskId, string content, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["taskId"] = taskId,
            ["content"] = content
        };
        await CallToolAsync("add_comment", args, cancellationToken);
    }

    public async Task PostLoungeMessageAsync(int? projectId, string content, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["content"] = content
        };

        if (projectId.HasValue)
        {
            args["projectId"] = projectId.Value;
        }

        await CallToolAsync("post_lounge_message", args, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _mcpClient.DisposeAsync();
    }

    private async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Calling MCP tool '{ToolName}'", toolName);

        var result = await _mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        var text = textContent?.Text ?? string.Empty;

        if (result.IsError == true)
        {
            _logger.LogWarning("MCP tool '{ToolName}' returned error: {Error}", toolName, text);
            throw new McpToolException(toolName, text);
        }

        _logger.LogDebug("MCP tool '{ToolName}' completed successfully", toolName);
        return text;
    }

    private T DeserializeResult<T>(string json, string toolName)
    {
        // Check if the response contains an error object
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetString() ?? "Unknown error";
                throw new McpToolException(toolName, errorMessage);
            }
        }
        catch (JsonException)
        {
            // Not valid JSON or not an error object — continue with deserialization
        }
        catch (McpToolException)
        {
            throw;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new McpToolException(toolName, "Failed to deserialize response.");
    }
}
