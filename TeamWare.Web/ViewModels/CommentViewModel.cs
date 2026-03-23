namespace TeamWare.Web.ViewModels;

public class CommentViewModel
{
    public int Id { get; set; }

    public int TaskItemId { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public string AuthorDisplayName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsEdited => UpdatedAt > CreatedAt.AddSeconds(1);

    public bool CanEditOrDelete { get; set; }

    public AttachmentListViewModel? Attachments { get; set; }
}
