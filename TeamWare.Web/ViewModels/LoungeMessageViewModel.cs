using TeamWare.Web.Services;

namespace TeamWare.Web.ViewModels;

public class LoungeMessageViewModel
{
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public string AuthorDisplayName { get; set; } = string.Empty;

    public string? AuthorAvatarUrl { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public bool IsEdited { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsPinned { get; set; }

    public string? PinnedByDisplayName { get; set; }

    public DateTime? PinnedAt { get; set; }

    public int? CreatedTaskId { get; set; }

    public List<ReactionSummary> Reactions { get; set; } = new();

    public bool CanEdit { get; set; }

    public bool CanDelete { get; set; }

    public bool CanPin { get; set; }

    public bool CanCreateTask { get; set; }
}
