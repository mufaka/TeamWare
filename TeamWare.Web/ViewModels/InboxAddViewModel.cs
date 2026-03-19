using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class InboxAddViewModel
{
    [Required]
    [StringLength(300, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}
