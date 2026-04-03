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
    /// Known read-only shell command names. These commands do not modify the
    /// file system or cause side effects and are safe to execute in dry-run mode.
    /// </summary>
    private static readonly HashSet<string> ReadOnlyShellCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "ls", "dir", "cat", "head", "tail", "less", "more",
        "find", "grep", "egrep", "fgrep", "rg",
        "wc", "file", "which", "where", "pwd",
        "echo", "env", "printenv",
        "whoami", "hostname", "uname", "date",
        "du", "df", "stat", "tree",
        "readlink", "realpath", "basename", "dirname",
        "id", "groups", "type", "test",
        "git status", "git log", "git diff", "git show", "git branch", "git remote", "git rev-parse",
    };

    /// <summary>
    /// Determines whether a permission request represents a read-only operation.
    /// </summary>
    internal static bool IsReadOnly(PermissionRequest request)
    {
        return request switch
        {
            PermissionRequestMcp mcp => mcp.ReadOnly,
            PermissionRequestShell shell => IsShellReadOnly(shell),
            PermissionRequestWrite => false, // File writes are never read-only
            _ => false // Unknown types default to write (deny in dry run)
        };
    }

    /// <summary>
    /// Determines whether a shell permission request is read-only by checking
    /// for write redirections, the SDK's command-level ReadOnly flags, and a
    /// safelist of known read-only executables.
    /// </summary>
    internal static bool IsShellReadOnly(PermissionRequestShell shell)
    {
        // Write redirection (> or >>) is always a write operation
        if (shell.HasWriteFileRedirection)
        {
            return false;
        }

        // If the SDK marks all commands as read-only, trust that
        if (shell.Commands.Length > 0 && shell.Commands.All(c => c.ReadOnly))
        {
            return true;
        }

        // Fall back to safelist: extract the executable name from the command text
        var commandName = ExtractCommandName(shell.FullCommandText);
        if (commandName is not null && ReadOnlyShellCommands.Contains(commandName))
        {
            return true;
        }

        // Also check multi-word commands (e.g. "git status")
        var twoWordCommand = ExtractTwoWordCommand(shell.FullCommandText);
        if (twoWordCommand is not null && ReadOnlyShellCommands.Contains(twoWordCommand))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the first token (executable name) from a command string.
    /// Handles paths like "/usr/bin/ls" by taking only the filename portion.
    /// </summary>
    internal static string? ExtractCommandName(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        // Take the first whitespace-delimited token
        var firstToken = commandText.AsSpan().TrimStart();
        var spaceIndex = firstToken.IndexOfAny(' ', '\t');
        if (spaceIndex >= 0)
        {
            firstToken = firstToken[..spaceIndex];
        }

        // Strip path prefix (e.g. /usr/bin/ls → ls)
        var slashIndex = firstToken.LastIndexOfAny(['/', '\\']);
        if (slashIndex >= 0)
        {
            firstToken = firstToken[(slashIndex + 1)..];
        }

        return firstToken.Length > 0 ? firstToken.ToString() : null;
    }

    /// <summary>
    /// Extracts the first two tokens from a command string (e.g. "git status").
    /// </summary>
    private static string? ExtractTwoWordCommand(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        var trimmed = commandText.AsSpan().TrimStart();
        var firstSpace = trimmed.IndexOfAny(' ', '\t');
        if (firstSpace < 0)
        {
            return null;
        }

        var rest = trimmed[(firstSpace + 1)..].TrimStart();
        var secondSpace = rest.IndexOfAny(' ', '\t');
        var secondToken = secondSpace >= 0 ? rest[..secondSpace] : rest;

        if (secondToken.Length == 0)
        {
            return null;
        }

        // Strip path from first token
        var firstToken = trimmed[..firstSpace];
        var slashIndex = firstToken.LastIndexOfAny(['/', '\\']);
        if (slashIndex >= 0)
        {
            firstToken = firstToken[(slashIndex + 1)..];
        }

        return $"{firstToken} {secondToken}";
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
