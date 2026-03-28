using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace TeamWare.Agent.Logging;

/// <summary>
/// Intercepts tool call permission requests and logs them instead of executing
/// write operations. Read tools execute normally; write tools are denied and logged.
/// See Specification CA-120 through CA-123.
/// </summary>
public class DryRunLogger
{
    private readonly ILogger _logger;
    private readonly List<DryRunToolCall> _loggedCalls = [];
    private readonly object _lock = new();

    public DryRunLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a snapshot of all logged tool calls.
    /// </summary>
    public IReadOnlyList<DryRunToolCall> LoggedCalls
    {
        get
        {
            lock (_lock)
            {
                return _loggedCalls.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Creates a <see cref="PermissionRequestHandler"/> delegate that blocks all write
    /// operations and logs them, while allowing read operations to proceed.
    /// </summary>
    public PermissionRequestHandler CreateHandler()
    {
        return (request, invocation) =>
        {
            if (IsReadOnly(request))
            {
                _logger.LogDebug(
                    "[DRY RUN] Allowing read operation: Kind={Kind}",
                    request.Kind);

                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Approved
                });
            }

            // Write operation — log and deny
            var call = CaptureToolCall(request);

            _logger.LogInformation(
                "[DRY RUN] Would execute write operation: Kind={Kind}, Details={Details}",
                request.Kind, call.Details);

            return Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.DeniedByRules
            });
        };
    }

    /// <summary>
    /// Determines whether a permission request represents a read-only operation.
    /// </summary>
    internal static bool IsReadOnly(PermissionRequest request)
    {
        return request switch
        {
            PermissionRequestMcp mcp => mcp.ReadOnly,
            PermissionRequestShell => false, // Shell commands are not read-only by default
            PermissionRequestWrite => false, // File writes are never read-only
            _ => false // Unknown types default to write (deny in dry run)
        };
    }

    private DryRunToolCall CaptureToolCall(PermissionRequest request)
    {
        var call = request switch
        {
            PermissionRequestShell shell => new DryRunToolCall
            {
                Kind = "Shell",
                Details = shell.FullCommandText ?? "(no command text)",
                Intention = shell.Intention
            },
            PermissionRequestMcp mcp => new DryRunToolCall
            {
                Kind = "MCP",
                Details = $"{mcp.ServerName}/{mcp.ToolName}",
                Intention = null
            },
            PermissionRequestWrite write => new DryRunToolCall
            {
                Kind = "Write",
                Details = write.FileName ?? "(no filename)",
                Intention = write.Intention
            },
            _ => new DryRunToolCall
            {
                Kind = request.Kind ?? "Unknown",
                Details = "(unknown request type)",
                Intention = null
            }
        };

        lock (_lock)
        {
            _loggedCalls.Add(call);
        }

        return call;
    }
}
