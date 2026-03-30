namespace TeamWare.Agent.Mcp;

public class AgentProfileConfiguration
{
    public int? PollingIntervalSeconds { get; set; }

    public string? Model { get; set; }

    public bool? AutoApproveTools { get; set; }

    public bool? DryRun { get; set; }

    public int? TaskTimeoutSeconds { get; set; }

    public string? SystemPrompt { get; set; }

    public string? RepositoryUrl { get; set; }

    public string? RepositoryBranch { get; set; }

    public string? RepositoryAccessToken { get; set; }

    public List<AgentProfileRepository>? Repositories { get; set; }

    public List<AgentProfileMcpServer>? McpServers { get; set; }
}
