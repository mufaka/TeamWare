namespace TeamWare.Agent.Mcp;

public class AgentProfileRepository
{
    public string ProjectName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    public string? AccessToken { get; set; }
}
