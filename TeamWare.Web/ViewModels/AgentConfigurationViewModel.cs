using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class AgentConfigurationViewModel
{
    [Display(Name = "Polling Interval (seconds)")]
    [Range(10, 3600, ErrorMessage = "Polling interval must be between 10 and 3600 seconds.")]
    public int? PollingIntervalSeconds { get; set; }

    [Display(Name = "Use default polling interval (60s)")]
    public bool PollingIntervalUseDefault { get; set; } = true;

    [Display(Name = "Model")]
    [StringLength(200, ErrorMessage = "Model name cannot exceed 200 characters.")]
    public string? Model { get; set; }

    [Display(Name = "Use default model")]
    public bool ModelUseDefault { get; set; } = true;

    [Display(Name = "Auto-Approve Tools")]
    public bool AutoApproveTools { get; set; } = true;

    [Display(Name = "Use default auto-approve (on)")]
    public bool AutoApproveToolsUseDefault { get; set; } = true;

    [Display(Name = "Dry Run")]
    public bool DryRun { get; set; }

    [Display(Name = "Use default dry run (off)")]
    public bool DryRunUseDefault { get; set; } = true;

    [Display(Name = "Task Timeout (seconds)")]
    [Range(60, 7200, ErrorMessage = "Task timeout must be between 60 and 7200 seconds.")]
    public int? TaskTimeoutSeconds { get; set; }

    [Display(Name = "Use default task timeout (600s)")]
    public bool TaskTimeoutUseDefault { get; set; } = true;

    [Display(Name = "System Prompt")]
    [StringLength(10000, ErrorMessage = "System prompt cannot exceed 10000 characters.")]
    public string? SystemPrompt { get; set; }

    [Display(Name = "Use default system prompt")]
    public bool SystemPromptUseDefault { get; set; } = true;

    [Display(Name = "Repository URL")]
    [StringLength(2000, ErrorMessage = "Repository URL cannot exceed 2000 characters.")]
    public string? RepositoryUrl { get; set; }

    [Display(Name = "Repository Branch")]
    [StringLength(200, ErrorMessage = "Repository branch cannot exceed 200 characters.")]
    public string? RepositoryBranch { get; set; }

    [Display(Name = "Repository Access Token")]
    [StringLength(500, ErrorMessage = "Access token cannot exceed 500 characters.")]
    public string? RepositoryAccessToken { get; set; }

    [Display(Name = "Masked Repository Access Token")]
    public string? RepositoryAccessTokenMasked { get; set; }
}
