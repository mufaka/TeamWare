using System.ComponentModel.DataAnnotations;

namespace TeamWare.Web.ViewModels;

public class CreateWhiteboardViewModel
{
    [Required]
    [StringLength(200, ErrorMessage = "The {0} must be at most {1} characters long.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;
}
