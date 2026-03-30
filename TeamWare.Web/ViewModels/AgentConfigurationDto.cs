namespace TeamWare.Web.ViewModels;

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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<AgentRepositoryDto> Repositories { get; set; } = [];

    public List<AgentMcpServerDto> McpServers { get; set; } = [];
}
