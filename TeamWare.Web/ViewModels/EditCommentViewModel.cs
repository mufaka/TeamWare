using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class EditCommentViewModel
{
    public int CommentId { get; set; }

    public int TaskItemId { get; set; }

    [Required]
    [StringLength(5000)]
    public string Content { get; set; } = string.Empty;
}
