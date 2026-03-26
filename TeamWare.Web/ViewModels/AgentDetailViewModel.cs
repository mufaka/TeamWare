using TeamWare.Web.Models;

namespace TeamWare.Web.ViewModels;

public class AgentDetailViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AgentDescription { get; set; }
    public bool IsAgentActive { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public List<PersonalAccessToken> Tokens { get; set; } = new();
    public List<AgentProjectMembership> ProjectMemberships { get; set; } = new();
    public List<AdminActivityLogEntryViewModel> RecentActivity { get; set; } = new();
}
