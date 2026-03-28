namespace TeamWare.Agent.Configuration;

public class McpServerOptions
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? AuthHeader { get; set; }
}
