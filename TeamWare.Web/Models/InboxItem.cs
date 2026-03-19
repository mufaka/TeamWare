using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class InboxItem
{
    public int Id { get; set; }

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public InboxItemStatus Status { get; set; } = InboxItemStatus.Unprocessed;

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public int? ConvertedToTaskId { get; set; }

    public TaskItem? ConvertedToTask { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
