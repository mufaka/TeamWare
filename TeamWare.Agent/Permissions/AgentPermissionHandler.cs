using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace TeamWare.Agent.Permissions;

/// <summary>
/// Custom permission handler that inspects each tool call before approving.
/// Optionally blocks dangerous operations (e.g., rm -rf, git push --force,
/// commits to main/master).
/// See Specification CA-131, CA-SEC-06.
/// </summary>
public class AgentPermissionHandler
{
    private readonly ILogger _logger;

    /// <summary>
    /// Shell command patterns that are always denied.
    /// </summary>
    private static readonly string[] DangerousShellPatterns =
    [
        "rm -rf",
        "rm -r /",
        "git push --force",
        "git push -f",
        "git checkout main",
        "git checkout master",
        "git merge main",
        "git merge master"
    ];

    public AgentPermissionHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a <see cref="PermissionRequestHandler"/> delegate that inspects
    /// each permission request and blocks dangerous operations.
    /// </summary>
    public PermissionRequestHandler CreateHandler()
    {
        return (request, invocation) =>
        {
            var (approved, reason) = Evaluate(request);

            if (approved)
            {
                _logger.LogDebug(
                    "Permission APPROVED: Kind={Kind}",
                    request.Kind);

                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Approved
                });
            }

            _logger.LogWarning(
                "Permission DENIED: Kind={Kind}, Reason={Reason}",
                request.Kind, reason);

            return Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.DeniedByRules
            });
        };
    }

    /// <summary>
    /// Evaluates a permission request and returns whether it should be approved
    /// along with a reason if denied.
    /// </summary>
    internal (bool Approved, string? Reason) Evaluate(PermissionRequest request)
    {
        if (request is PermissionRequestShell shellRequest)
        {
            return EvaluateShellRequest(shellRequest);
        }

        // For all other request types (MCP, Write, etc.), approve by default
        return (true, null);
    }

    private static (bool Approved, string? Reason) EvaluateShellRequest(PermissionRequestShell shellRequest)
    {
        var command = shellRequest.FullCommandText;
        if (string.IsNullOrWhiteSpace(command))
        {
            return (true, null);
        }

        foreach (var pattern in DangerousShellPatterns)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Dangerous shell command blocked: contains '{pattern}'");
            }
        }

        return (true, null);
    }
}
