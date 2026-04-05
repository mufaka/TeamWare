namespace TeamWare.Web.Models;

public class AgentTaskAssignmentPermission
{
    public int Id { get; set; }

    public string AgentUserId { get; set; } = string.Empty;

    public ApplicationUser AgentUser { get; set; } = null!;

    public string AllowedAssignerUserId { get; set; } = string.Empty;

    public ApplicationUser AllowedAssignerUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
