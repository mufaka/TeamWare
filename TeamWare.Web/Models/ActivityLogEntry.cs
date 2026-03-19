using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class ActivityLogEntry
{
    public int Id { get; set; }

    public int TaskItemId { get; set; }

    public TaskItem TaskItem { get; set; } = null!;

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public ActivityChangeType ChangeType { get; set; }

    [StringLength(500)]
    public string? OldValue { get; set; }

    [StringLength(500)]
    public string? NewValue { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
