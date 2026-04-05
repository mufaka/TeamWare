using Microsoft.Extensions.Logging;
using TeamWare.Agent.Mcp;

namespace TeamWare.Agent.Configuration;

public class AgentIdentityOptions
{
    public string Name { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string? RepositoryUrl { get; set; }
    public string? RepositoryBranch { get; set; }
    public string? RepositoryAccessToken { get; set; }
    public string PersonalAccessToken { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 60;
    public string? Model { get; set; }
    public bool AutoApproveTools { get; set; } = true;
    public bool DryRun { get; set; }
    public int TaskTimeoutSeconds { get; set; } = 600;
    public string? SystemPrompt { get; set; }
    public List<RepositoryOptions> Repositories { get; set; } = [];
    public List<McpServerOptions> McpServers { get; set; } = [];

    /// <summary>
    /// Applies server-side configuration from the agent's profile, merging with local settings.
    /// Local values always take precedence: server values are only applied when the local value
    /// is at its default. WorkingDirectory and PersonalAccessToken are never overwritten.
    /// </summary>
    public void ApplyServerConfiguration(AgentProfileConfiguration? serverConfig, ILogger? logger = null)
    {
        if (serverConfig is null)
        {
            return;
        }

        // Behavioral fields: apply server value only when local is at default
        if (PollingIntervalSeconds == 60 && serverConfig.PollingIntervalSeconds.HasValue)
        {
            PollingIntervalSeconds = serverConfig.PollingIntervalSeconds.Value;
        }

        if (Model is null && serverConfig.Model is not null)
        {
            Model = serverConfig.Model;
        }

        if (AutoApproveTools == true && serverConfig.AutoApproveTools.HasValue)
        {
            AutoApproveTools = serverConfig.AutoApproveTools.Value;
        }

        if (DryRun == false && serverConfig.DryRun.HasValue)
        {
            DryRun = serverConfig.DryRun.Value;
        }

        if (TaskTimeoutSeconds == 600 && serverConfig.TaskTimeoutSeconds.HasValue)
        {
            TaskTimeoutSeconds = serverConfig.TaskTimeoutSeconds.Value;
        }

        if (SystemPrompt is null && serverConfig.SystemPrompt is not null)
        {
            SystemPrompt = serverConfig.SystemPrompt;
        }

        // Flat repo fields: apply if local is null
        if (RepositoryUrl is null && serverConfig.RepositoryUrl is not null)
        {
            RepositoryUrl = serverConfig.RepositoryUrl;
        }

        if (RepositoryBranch is null && serverConfig.RepositoryBranch is not null)
        {
            RepositoryBranch = serverConfig.RepositoryBranch;
        }

        if (RepositoryAccessToken is null && serverConfig.RepositoryAccessToken is not null)
        {
            RepositoryAccessToken = serverConfig.RepositoryAccessToken;
        }

        // Repositories: match by ProjectName (case-insensitive), local wins on collision
        if (serverConfig.Repositories is { Count: > 0 })
        {
            var localNames = new HashSet<string>(
                Repositories.Select(r => r.ProjectName),
                StringComparer.OrdinalIgnoreCase);

            foreach (var serverRepo in serverConfig.Repositories)
            {
                if (!localNames.Contains(serverRepo.ProjectName))
                {
                    Repositories.Add(new RepositoryOptions
                    {
                        ProjectName = serverRepo.ProjectName,
                        Url = serverRepo.Url,
                        Branch = serverRepo.Branch,
                        AccessToken = serverRepo.AccessToken
                    });
                }
            }
        }

        // MCP Servers: match by Name (case-insensitive), local wins on collision
        if (serverConfig.McpServers is { Count: > 0 })
        {
            var localNames = new HashSet<string>(
                McpServers.Select(s => s.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var serverMcp in serverConfig.McpServers)
            {
                if (!localNames.Contains(serverMcp.Name))
                {
                    McpServers.Add(new McpServerOptions
                    {
                        Name = serverMcp.Name,
                        Type = serverMcp.Type,
                        Url = serverMcp.Url,
                        AuthHeader = serverMcp.AuthHeader,
                        Command = serverMcp.Command,
                        Args = serverMcp.Args ?? [],
                        Env = serverMcp.Env ?? []
                    });
                }
            }
        }

        logger?.LogDebug(
            "Server configuration merged for agent '{AgentName}': " +
            "PollingInterval={Polling}s, Model={Model}, AutoApprove={AutoApprove}, " +
            "DryRun={DryRun}, TaskTimeout={Timeout}s, " +
            "Repositories={RepoCount}, McpServers={McpCount}",
            Name, PollingIntervalSeconds, Model ?? "(null)", AutoApproveTools,
            DryRun, TaskTimeoutSeconds,
            Repositories.Count, McpServers.Count);
    }

    /// <summary>
    /// Resolves the repository configuration and working directory for a given project name.
    /// If a matching entry exists in <see cref="Repositories"/>, the working directory is
    /// a sanitized subdirectory under <see cref="WorkingDirectory"/>.
    /// Otherwise, falls back to the flat RepositoryUrl/WorkingDirectory fields.
    /// </summary>
    /// <returns>The resolved repository URL (may be null), branch, access token, and working directory path.</returns>
    public ResolvedRepository ResolveRepository(string? projectName)
    {
        if (!string.IsNullOrWhiteSpace(projectName) && Repositories.Count > 0)
        {
            var match = Repositories.FirstOrDefault(r =>
                r.ProjectName.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                var subDir = SanitizeDirectoryName(match.ProjectName);
                var repoWorkDir = Path.Combine(WorkingDirectory, subDir);

                return new ResolvedRepository(
                    match.Url,
                    match.Branch,
                    match.AccessToken,
                    repoWorkDir);
            }
        }

        // Fallback to the flat (legacy) configuration.
        // Use a "default" subdirectory so the default repo doesn't conflict
        // with project-specific subdirectories under WorkingDirectory.
        return new ResolvedRepository(
            RepositoryUrl,
            RepositoryBranch ?? "main",
            RepositoryAccessToken,
            Path.Combine(WorkingDirectory, "default"));
    }

    internal static string SanitizeDirectoryName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}
