using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class EditAgentViewModel
{
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(256, ErrorMessage = "Display name cannot exceed 256 characters.")]
    [Display(Name = "Display Name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; }

    public List<string> AllowedAssignerUserIds { get; set; } = [];

    public List<AgentAssignmentPermissionOptionViewModel> AllowedAssignerOptions { get; set; } = [];

    public AgentConfigurationViewModel Configuration { get; set; } = new();

    public List<ProjectOptionViewModel> AvailableProjects { get; set; } = [];

    public List<AgentRepositoryDto> Repositories { get; set; } = [];

    public List<AgentMcpServerDto> McpServers { get; set; } = [];
}

public class AgentAssignmentPermissionOptionViewModel
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
