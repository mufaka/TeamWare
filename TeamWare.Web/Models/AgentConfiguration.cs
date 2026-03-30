using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class AgentConfiguration
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public int? PollingIntervalSeconds { get; set; }

    [StringLength(200)]
    public string? Model { get; set; }

    public bool? AutoApproveTools { get; set; }

    public bool? DryRun { get; set; }

    public int? TaskTimeoutSeconds { get; set; }

    [StringLength(10000)]
    public string? SystemPrompt { get; set; }

    [StringLength(500)]
    public string? RepositoryUrl { get; set; }

    [StringLength(200)]
    public string? RepositoryBranch { get; set; }

    [StringLength(2000)]
    public string? EncryptedRepositoryAccessToken { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AgentRepository> Repositories { get; set; } = new List<AgentRepository>();

    public ICollection<AgentMcpServer> McpServers { get; set; } = new List<AgentMcpServer>();
}
