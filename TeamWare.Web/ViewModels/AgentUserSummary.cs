namespace TeamWare.Web.ViewModels;

public class AgentUserSummary
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AgentDescription { get; set; }

    public bool IsAgentActive { get; set; }

    public DateTime? LastActiveAt { get; set; }

    public int AssignedTaskCount { get; set; }
}
