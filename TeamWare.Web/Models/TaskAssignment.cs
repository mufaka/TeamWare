namespace TeamWare.Web.Models;

public class TaskAssignment
{
    public int Id { get; set; }

    public int TaskItemId { get; set; }

    public TaskItem TaskItem { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
