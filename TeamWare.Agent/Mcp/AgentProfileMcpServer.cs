namespace TeamWare.Agent.Mcp;

public class AgentProfileMcpServer
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? AuthHeader { get; set; }

    public string? Command { get; set; }

    public List<string>? Args { get; set; }

    public Dictionary<string, string>? Env { get; set; }
}
