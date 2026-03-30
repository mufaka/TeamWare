using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class AgentRepository
{
    public int Id { get; set; }

    public int AgentConfigurationId { get; set; }

    public AgentConfiguration AgentConfiguration { get; set; } = null!;

    [Required]
    [StringLength(200)]
    public string ProjectName { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Url { get; set; } = string.Empty;

    [StringLength(200)]
    public string Branch { get; set; } = "main";

    [StringLength(2000)]
    public string? EncryptedAccessToken { get; set; }

    public int DisplayOrder { get; set; }
}
