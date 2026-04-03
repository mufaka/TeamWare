namespace TeamWare.Web.ViewModels;

/// <summary>
/// Read-only representation of an agent's server-side configuration.
/// Secrets are either masked (for admin UI display) or fully decrypted (for MCP tool responses),
/// depending on which service method was used to load it.
/// </summary>
public class AgentConfigurationDto
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<AgentRepositoryDto> Repositories { get; set; } = [];

    public List<AgentMcpServerDto> McpServers { get; set; } = [];
}
