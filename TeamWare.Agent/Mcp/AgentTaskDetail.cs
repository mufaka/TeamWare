namespace TeamWare.Agent.Mcp;

public class AgentTaskDetail
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public bool IsNextAction { get; set; }
    public bool IsSomedayMaybe { get; set; }
    public int ProjectId { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public List<AgentTaskAssignee> Assignees { get; set; } = [];
    public List<AgentTaskComment> Comments { get; set; } = [];
}
