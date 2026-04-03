namespace TeamWare.Web.ViewModels;

/// <summary>
/// Input DTO for saving an agent's behavioral configuration fields.
/// Repositories and MCP servers are managed through separate CRUD operations.
/// </summary>
public class SaveAgentConfigurationDto
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

    public string? AgentBackend { get; set; }

    public string? CodexApiKey { get; set; }

    public bool ClearCodexApiKey { get; set; }
}
