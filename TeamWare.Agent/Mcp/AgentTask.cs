namespace TeamWare.Agent.Mcp;

public class AgentTask
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public int ProjectId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsNextAction { get; set; }
}
