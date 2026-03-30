namespace TeamWare.Agent.Mcp;

public class AgentProfile
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAgent { get; set; }
    public string? AgentDescription { get; set; }
    public bool? IsAgentActive { get; set; }
    public string? LastActiveAt { get; set; }
    public AgentProfileConfiguration? Configuration { get; set; }
}
