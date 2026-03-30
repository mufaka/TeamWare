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

        // Fallback to the flat (legacy) configuration
        return new ResolvedRepository(
            RepositoryUrl,
            RepositoryBranch ?? "main",
            RepositoryAccessToken,
            WorkingDirectory);
    }

    internal static string SanitizeDirectoryName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}
