using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class AddCommentViewModel
{
    public int TaskItemId { get; set; }

    [Required]
    [StringLength(5000)]
    public string Content { get; set; } = string.Empty;
}
