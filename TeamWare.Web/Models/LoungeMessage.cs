using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.Models;

public class LoungeMessage
{
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsEdited { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsPinned { get; set; }

    public string? PinnedByUserId { get; set; }

    public ApplicationUser? PinnedByUser { get; set; }

    public DateTime? PinnedAt { get; set; }

    public int? CreatedTaskId { get; set; }

    public TaskItem? CreatedTask { get; set; }

    public ICollection<LoungeReaction> Reactions { get; set; } = new List<LoungeReaction>();
}
