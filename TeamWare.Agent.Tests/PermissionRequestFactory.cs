using GitHub.Copilot.SDK;

namespace TeamWare.Agent.Tests;

/// <summary>
/// Factory helpers that construct SDK permission-request types with all
/// required members populated so tests don't have to repeat the boilerplate.
/// </summary>
internal static class PermissionRequestFactory
{
    public static PermissionRequestShell Shell(
        string? fullCommandText = null,
        string? intention = null,
        bool hasWriteFileRedirection = false,
        PermissionRequestShellCommandsItem[]? commands = null)
    {
        return new PermissionRequestShell
        {
            FullCommandText = fullCommandText,
            Intention = intention ?? string.Empty,
            Commands = commands ?? [],
            PossiblePaths = [],
            PossibleUrls = [],
            HasWriteFileRedirection = hasWriteFileRedirection,
            CanOfferSessionApproval = false,
        };
    }

    public static PermissionRequestMcp Mcp(
        string? toolName = null,
        string? serverName = null,
        bool readOnly = false,
        string? toolTitle = null)
    {
        return new PermissionRequestMcp
        {
            ToolName = toolName ?? string.Empty,
            ServerName = serverName ?? string.Empty,
            ToolTitle = toolTitle ?? string.Empty,
            ReadOnly = readOnly,
        };
    }

    public static PermissionRequestWrite Write(
        string? fileName = null,
        string? intention = null)
    {
        return new PermissionRequestWrite
        {
            FileName = fileName,
            Intention = intention ?? string.Empty,
            Diff = string.Empty,
        };
    }
}
