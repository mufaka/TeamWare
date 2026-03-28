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
    public string? SystemPrompt { get; set; }
    public List<McpServerOptions> McpServers { get; set; } = [];
}
