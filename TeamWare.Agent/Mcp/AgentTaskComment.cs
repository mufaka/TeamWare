namespace TeamWare.Agent.Mcp;

public class AgentTaskComment
{
    public int Id { get; set; }
    public string? AuthorName { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}
