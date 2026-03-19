using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class TaskItem
{
    public int Id { get; set; }

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; } = TaskItemStatus.ToDo;

    public TaskItemPriority Priority { get; set; } = TaskItemPriority.Medium;

    public DateTime? DueDate { get; set; }

    public bool IsNextAction { get; set; }

    public bool IsSomedayMaybe { get; set; }

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string CreatedByUserId { get; set; } = string.Empty;

    public ApplicationUser CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TaskAssignment> Assignments { get; set; } = new List<TaskAssignment>();

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
