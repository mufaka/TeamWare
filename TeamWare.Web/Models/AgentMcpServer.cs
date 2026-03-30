using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class AgentMcpServer
{
    public int Id { get; set; }

    public int AgentConfigurationId { get; set; }

    public AgentConfiguration AgentConfiguration { get; set; } = null!;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Type { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Url { get; set; }

    [StringLength(2000)]
    public string? EncryptedAuthHeader { get; set; }

    [StringLength(500)]
    public string? Command { get; set; }

    [StringLength(4000)]
    public string? Args { get; set; }

    [StringLength(8000)]
    public string? EncryptedEnv { get; set; }

    public int DisplayOrder { get; set; }
}
